using Casino.Hosts.Api;
using Casino.Hosts.Wallet;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Yarp.ReverseProxy.Forwarder;

namespace Casino.Integration.Tests;

/// <summary>
/// El sistema desplegado, en memoria: cada servicio es un host REAL e independiente (su propia composicion, su propia base de datos y
/// su propia conexion a RabbitMQ) y se hablan igual que en produccion. Las llamadas HTTP entran por el gateway (la API), que reenvia
/// con el proxy real (YARP) a la Wallet; lo que cruza servicios viaja por RabbitMQ. Lo unico que cambia es que el "cable" entre el
/// gateway y la Wallet es memoria en lugar de red.
/// </summary>
public sealed class CasinoCluster : IDisposable, IAsyncDisposable
{
    /// <summary>La direccion ficticia de la Wallet que ve el gateway; el cable en memoria la ignora.</summary>
    public const string WalletAddress = "http://wallet.test";

    public CasinoCluster(WebApplicationFactory<ApiEntryPoint> api, WebApplicationFactory<WalletServiceEntryPoint> wallet)
    {
        Api = api;
        Wallet = wallet;
        Services = new ClusterServices(api.Services, wallet.Services);
    }

    /// <summary>El host principal: puerta de entrada (gateway), usuarios, juegos y tiempo real.</summary>
    public WebApplicationFactory<ApiEntryPoint> Api { get; }

    /// <summary>El servicio de la Wallet.</summary>
    public WebApplicationFactory<WalletServiceEntryPoint> Wallet { get; }

    /// <summary>
    /// Resuelve un servicio en el host que lo tiene (la API primero, despues la Wallet). Cada servicio de negocio vive en uno solo;
    /// lo que existe en ambos (por ejemplo el IHost) se resuelve en la API: para elegir, usar <see cref="Api"/> o <see cref="Wallet"/>.
    /// </summary>
    public IServiceProvider Services { get; }

    public TestServer Server => Api.Server;

    /// <summary>Un cliente que entra por el gateway, como un navegador.</summary>
    public HttpClient CreateClient() => Api.CreateClient();

    public void Dispose()
    {
        Api.Dispose();
        Wallet.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await Api.DisposeAsync();
        await Wallet.DisposeAsync();
    }

    /// <summary>El cable entre el gateway y la Wallet: en vez de abrir un puerto, el proxy envia la peticion al servidor de pruebas de la Wallet.</summary>
    internal sealed class InMemoryForwarderHttpClientFactory(HttpMessageHandler walletHandler) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(walletHandler, disposeHandler: false);
    }

    private sealed class ClusterServices(params IServiceProvider[] providers) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            foreach (var provider in providers)
            {
                if (provider.GetService(serviceType) is { } service)
                {
                    return service;
                }
            }

            return null;
        }
    }
}
