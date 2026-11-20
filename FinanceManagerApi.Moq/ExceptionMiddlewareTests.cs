using System.Text;
using FinanceManagerApi.Middlewares;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceManagerApi.Moq;

public sealed class ExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldIgnoreACancellationCausedByTheClientHangingUp()
    {
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();

        var context = new DefaultHttpContext();
        context.RequestAborted = aborted.Token;
        context.Response.Body = new MemoryStream();

        var middleware = Build(_ => throw new OperationCanceledException(aborted.Token));

        // The client is gone: this must not surface as an error or a 500.
        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Empty(ReadBody(context));
    }

    [Fact]
    public async Task InvokeAsync_ShouldStillReportA500WhenTheClientIsConnected()
    {
        // No cancellation on RequestAborted: an OperationCanceledException here is
        // an upstream timeout, which the caller does need to hear about.
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = Build(_ => throw new OperationCanceledException("upstream timed out"));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains("upstream timed out", ReadBody(context), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_ShouldSerialiseAnUnhandledExceptionAsJson()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = Build(_ => throw new InvalidOperationException("boom"));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);

        var body = ReadBody(context);
        Assert.Contains("boom", body);
        Assert.Contains("statusCode", body);
    }

    private static ExceptionMiddleware Build(RequestDelegate next) => new(
        NullLogger<ExceptionMiddleware>.Instance,
        next,
        new FakeHostEnvironment());

    private static string ReadBody(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "FinanceManagerApi.Moq";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
