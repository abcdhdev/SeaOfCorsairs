import argparse
import getpass
import re
import shlex
import sys
import tarfile
import tempfile
from pathlib import Path

import paramiko


def repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


def read_bundle_version(root: Path) -> str:
    content = (root / "ProjectSettings" / "ProjectSettings.asset").read_text(encoding="utf-8")
    match = re.search(r"^\s*bundleVersion:\s*(.+)$", content, re.MULTILINE)
    if not match:
        raise RuntimeError("Could not find bundleVersion in ProjectSettings.asset")
    return match.group(1).strip()


def read_protocol_version(root: Path) -> str:
    content = (root / "Assets" / "Scripts" / "NetcodeConnectionPayload.cs").read_text(encoding="utf-8")
    match = re.search(r"CurrentProtocolVersion\s*=\s*(\d+)", content)
    if not match:
        raise RuntimeError("Could not find CurrentProtocolVersion in NetcodeConnectionPayload.cs")
    return match.group(1)


def make_archive(source_dir: Path) -> Path:
    temp = tempfile.NamedTemporaryFile(prefix="linux-server-build-", suffix=".tar.gz", delete=False)
    temp.close()
    archive_path = Path(temp.name)

    with tarfile.open(archive_path, "w:gz") as archive:
        archive.add(source_dir, arcname="LinuxServerBuild")

    return archive_path


def connect_ssh(args: argparse.Namespace) -> paramiko.SSHClient:
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())

    connect_kwargs = {
        "hostname": args.host,
        "username": args.user,
        "port": args.port,
        "timeout": 30,
        "look_for_keys": True,
        "allow_agent": True,
    }

    if args.key_path:
        connect_kwargs["key_filename"] = str(Path(args.key_path).expanduser())
    elif args.password:
        connect_kwargs["password"] = args.password

    client.connect(**connect_kwargs)
    return client


def run(client: paramiko.SSHClient, command: str, timeout: int = 600) -> str:
    stdin, stdout, stderr = client.exec_command(command, timeout=timeout)
    exit_code = stdout.channel.recv_exit_status()
    out = stdout.read().decode("utf-8", "replace")
    err = stderr.read().decode("utf-8", "replace")

    if out:
        print(out, end="" if out.endswith("\n") else "\n")
    if err:
        print(err, end="" if err.endswith("\n") else "\n", file=sys.stderr)

    if exit_code != 0:
        raise RuntimeError(f"Remote command failed ({exit_code}): {command}")

    return out


def update_env_file(sftp: paramiko.SFTPClient, env_path: str, updates: dict[str, str]) -> None:
    try:
        with sftp.file(env_path, "r") as env_file:
            current = env_file.read().decode("utf-8")
    except FileNotFoundError:
        current = ""

    lines = [line for line in current.splitlines() if line.strip()]
    seen: set[str] = set()
    new_lines: list[str] = []

    for line in lines:
        if "=" not in line:
            new_lines.append(line)
            continue

        key, _, _ = line.partition("=")
        if key in updates:
            new_lines.append(f"{key}={updates[key]}")
            seen.add(key)
        else:
            new_lines.append(line)

    for key, value in updates.items():
        if key not in seen:
            new_lines.append(f"{key}={value}")

    with sftp.file(env_path, "w") as env_file:
        env_file.write(("\n".join(new_lines) + "\n").encode("utf-8"))


def main() -> int:
    parser = argparse.ArgumentParser(description="Deploy the Linux dedicated server build to the remote Sea Wars host.")
    parser.add_argument("--host", required=True, help="Remote server IP or hostname.")
    parser.add_argument("--user", default="ubuntu", help="SSH username.")
    parser.add_argument("--port", type=int, default=22, help="SSH port.")
    parser.add_argument("--password", help="SSH password. Omit to use key auth or interactive prompt.")
    parser.add_argument("--key-path", help="Path to the private SSH key.")
    parser.add_argument("--remote-root", default="/opt/seawars", help="Remote project root.")
    parser.add_argument("--skip-env-sync", action="store_true", help="Do not sync required client/protocol versions into remote .env.")
    args = parser.parse_args()

    root = repo_root()
    build_dir = root / "Builds" / "LinuxServerBuild"
    if not build_dir.exists():
        raise RuntimeError(f"Build directory not found: {build_dir}")

    if not args.password and not args.key_path:
        args.password = getpass.getpass(f"SSH password for {args.user}@{args.host}: ")

    bundle_version = read_bundle_version(root)
    protocol_version = read_protocol_version(root)
    archive_path = make_archive(build_dir)

    remote_root = args.remote_root.rstrip("/")
    remote_archive = f"{remote_root}/linux-server-build.tar.gz"
    remote_env = f"{remote_root}/.env"

    print(f"Local bundle version: {bundle_version}")
    print(f"Local protocol version: {protocol_version}")
    print(f"Uploading archive: {archive_path}")

    client = connect_ssh(args)
    try:
        run(client, f"mkdir -p {shlex.quote(remote_root)} {shlex.quote(remote_root + '/Builds')} {shlex.quote(remote_root + '/.incoming')}")

        sftp = client.open_sftp()
        try:
            sftp.put(str(archive_path), remote_archive)
            if not args.skip_env_sync:
                update_env_file(
                    sftp,
                    remote_env,
                    {
                        "SEAWARS_REQUIRED_PROTOCOL_VERSION": protocol_version,
                        "SEAWARS_REQUIRED_CLIENT_VERSION": bundle_version,
                    },
                )
        finally:
            sftp.close()

        run(
            client,
            " && ".join(
                [
                    f"cd {shlex.quote(remote_root)}",
                    "rm -rf .incoming/LinuxServerBuild",
                    "mkdir -p .incoming",
                    "tar -xzf linux-server-build.tar.gz -C .incoming",
                    "rm -rf Builds/LinuxServerBuild",
                    "mv .incoming/LinuxServerBuild Builds/LinuxServerBuild",
                    "rm -f linux-server-build.tar.gz",
                ]
            ),
            timeout=1800,
        )

        compose = f"sudo docker compose -f {shlex.quote(remote_root + '/docker-compose.yml')} -f {shlex.quote(remote_root + '/docker-compose.server.yml')}"
        run(client, f"cd {shlex.quote(remote_root)} && {compose} build game-server", timeout=3600)
        run(client, f"cd {shlex.quote(remote_root)} && {compose} up -d --no-deps game-server", timeout=1800)
        run(client, f"cd {shlex.quote(remote_root)} && {compose} ps game-server", timeout=300)
        run(client, f"cd {shlex.quote(remote_root)} && {compose} logs --tail=40 game-server", timeout=300)
    finally:
        client.close()
        archive_path.unlink(missing_ok=True)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
