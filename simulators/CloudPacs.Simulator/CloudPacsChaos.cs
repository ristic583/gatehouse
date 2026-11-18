using Microsoft.Extensions.Options;

namespace CloudPacs.Simulator;

/// <summary>Injected vendor failures: counters first, then random rates.</summary>
public sealed class CloudPacsChaos(IOptions<CloudPacsOptions> options)
{
    private readonly Lock _gate = new();
    private int _failNext;
    private int _rateLimitNext;
    private double _errorRate = options.Value.ErrorRate;
    private double _rateLimitRate = options.Value.RateLimitRate;

    public void Apply(ChaosRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            _failNext = request.FailNext ?? _failNext;
            _rateLimitNext = request.RateLimitNext ?? _rateLimitNext;
            _errorRate = request.ErrorRate ?? _errorRate;
            _rateLimitRate = request.RateLimitRate ?? _rateLimitRate;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            (_failNext, _rateLimitNext, _errorRate, _rateLimitRate) = (0, 0, 0, 0);
        }
    }

    /// <summary>Returns 503, 429 or null (no failure) for the next API call.</summary>
    public int? NextFailure()
    {
        lock (_gate)
        {
            if (_rateLimitNext > 0)
            {
                _rateLimitNext--;
                return StatusCodes.Status429TooManyRequests;
            }

            if (_failNext > 0)
            {
                _failNext--;
                return StatusCodes.Status503ServiceUnavailable;
            }

#pragma warning disable CA5394 // simulated chaos, not security
            if (_rateLimitRate > 0 && Random.Shared.NextDouble() < _rateLimitRate)
            {
                return StatusCodes.Status429TooManyRequests;
            }

            return _errorRate > 0 && Random.Shared.NextDouble() < _errorRate ? StatusCodes.Status503ServiceUnavailable : null;
#pragma warning restore CA5394
        }
    }
}
