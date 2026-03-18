namespace MMS.Http;

/// <summary>
/// Fluent builder for registering minimal API endpoints with a compact, readable syntax.
/// </summary>
public sealed class EndpointBuilder(IEndpointRouteBuilder routes)
{
    private string _method = "GET";
    private string _route = "/";
    private Delegate? _handler;
    private string? _name;
    private string? _rateLimitingPolicy;

    /// <summary>
    /// Configures the endpoint as an HTTP GET route.
    /// </summary>
    /// <param name="route">The route pattern to map.</param>
    /// <returns>The same builder for chaining.</returns>
    public EndpointBuilder Get(string route)
    {
        _method = "GET";
        _route = route;
        return this;
    }

    /// <summary>
    /// Configures the endpoint as an HTTP POST route.
    /// </summary>
    /// <param name="route">The route pattern to map.</param>
    /// <returns>The same builder for chaining.</returns>
    public EndpointBuilder Post(string route)
    {
        _method = "POST";
        _route = route;
        return this;
    }

    /// <summary>
    /// Configures the endpoint as an HTTP DELETE route.
    /// </summary>
    /// <param name="route">The route pattern to map.</param>
    /// <returns>The same builder for chaining.</returns>
    public EndpointBuilder Delete(string route)
    {
        _method = "DELETE";
        _route = route;
        return this;
    }

    /// <summary>
    /// Configures the endpoint as a generic mapped route, useful for WebSocket handlers.
    /// </summary>
    /// <param name="route">The route pattern to map.</param>
    /// <returns>The same builder for chaining.</returns>
    public EndpointBuilder Map(string route)
    {
        _method = "MAP";
        _route = route;
        return this;
    }

    /// <summary>
    /// Sets the request handler delegate.
    /// </summary>
    /// <param name="handler">The delegate to invoke when the endpoint matches.</param>
    /// <returns>The same builder for chaining.</returns>
    public EndpointBuilder Handler(Delegate handler)
    {
        _handler = handler;
        return this;
    }

    /// <summary>
    /// Sets the endpoint name.
    /// </summary>
    /// <param name="name">The endpoint name.</param>
    /// <returns>The same builder for chaining.</returns>
    public EndpointBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>
    /// Applies an ASP.NET Core rate-limiting policy to the endpoint.
    /// </summary>
    /// <param name="policyName">The name of the rate-limiting policy.</param>
    /// <returns>The same builder for chaining.</returns>
    public EndpointBuilder RequireRateLimiting(string policyName)
    {
        _rateLimitingPolicy = policyName;
        return this;
    }

    /// <summary>
    /// Builds and registers the configured endpoint.
    /// </summary>
    /// <returns>The same route builder that created this endpoint.</returns>
    public void Build()
    {
        ArgumentNullException.ThrowIfNull(_handler);

        var endpoint = _method switch
        {
            "GET" => routes.MapGet(_route, _handler),
            "POST" => routes.MapPost(_route, _handler),
            "DELETE" => routes.MapDelete(_route, _handler),
            "MAP" => routes.Map(_route, _handler),
            _ => throw new NotSupportedException($"Method {_method} is not supported.")
        };

        if (_name is not null)
            endpoint.WithName(_name);

        if (_rateLimitingPolicy is not null)
            endpoint.RequireRateLimiting(_rateLimitingPolicy);
    }
}

/// <summary>
/// Extension methods for starting fluent endpoint registrations.
/// </summary>
internal static class FluentEndpointBuilderExtensions
{
    /// <summary>
    /// Starts building an endpoint on a web application.
    /// </summary>
    /// <param name="app">The application to map onto.</param>
    /// <returns>A new endpoint builder.</returns>
    public static EndpointBuilder Endpoint(this WebApplication app) => new(app);

    /// <summary>
    /// Starts building an endpoint on a grouped route builder.
    /// </summary>
    /// <param name="group">The route group to map onto.</param>
    /// <returns>A new endpoint builder.</returns>
    public static EndpointBuilder Endpoint(this RouteGroupBuilder group) => new(group);
}
