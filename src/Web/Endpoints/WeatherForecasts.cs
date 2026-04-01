using CleanArchitecture.Application.WeatherForecasts.Queries.GetWeatherForecasts;
using LiteBus.Queries.Abstractions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CleanArchitecture.Web.Endpoints;

public class WeatherForecasts : IEndpointGroup
{
    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();

        groupBuilder.MapGet(GetWeatherForecasts);
    }

    [EndpointSummary("Get Weather Forecasts")]
    [EndpointDescription("Retrieves a list of weather forecasts for the next few days.")]
    public static async Task<Ok<IEnumerable<WeatherForecast>>> GetWeatherForecasts(IQueryMediator mediator)
    {
        var forecasts = await mediator.QueryAsync(new GetWeatherForecastsQuery());

        return TypedResults.Ok(forecasts);
    }
}
