using System;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Banks.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace CardStatement.Api.Tests;

public sealed class EmptyRegistryStartupTests
{
    private sealed class EmptyRegistryFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBankProvider>();
            });
        }
    }

    [Fact]
    public void RegistryResolution_WithNoProvidersRegistered_ThrowsEmptyBankRegistryException()
    {
        // Arrange
        using var factory = new EmptyRegistryFactory();

        // Act
        Action act = () =>
        {
            // Accessing Services initializes the host and executes Program.cs, which contains:
            // var registry = app.Services.GetRequiredService<IBankRegistry>();
            _ = factory.Services.GetRequiredService<IBankRegistry>();
        };

        // Assert
        var exception = Record.Exception(act);
        exception.Should().NotBeNull();
        
        // Traverse exception tree to find EmptyBankRegistryException
        var found = false;
        var current = exception;
        while (current != null)
        {
            if (current is EmptyBankRegistryException)
            {
                found = true;
                break;
            }
            current = current.InnerException;
        }

        found.Should().BeTrue($"EmptyBankRegistryException must be present in the exception chain, but got: {exception}");
    }
}
