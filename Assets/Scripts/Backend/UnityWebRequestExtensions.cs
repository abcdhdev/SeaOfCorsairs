using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

public static class UnityWebRequestExtensions
{
    public static async Task SendWebRequestAsync(this UnityWebRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var op = request.SendWebRequest();
        while (!op.isDone)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                request.Abort();
                cancellationToken.ThrowIfCancellationRequested();
            }

            await Task.Yield();
        }
    }
}

