namespace SeaWars.PlayerDataService.Options;

public sealed class S3Options
{
    public string ServiceUrl { get; set; } = string.Empty;
    public bool ForcePathStyle { get; set; } = true;
    public string Region { get; set; } = "us-east-1";

    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    public S3Buckets Buckets { get; set; } = new();
}

public sealed class S3Buckets
{
    public string Assets { get; set; } = "seawars-assets";
    public string Logs { get; set; } = "seawars-logs";
}

