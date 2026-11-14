using System.Security.Claims;
using System.Security.Cryptography;
using HubPedidos.Notificacoes.TempoReal;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HubPedidos.Api.Seguranca;

/// <summary>
/// JWT bearer. A chave de assinatura vem de "Autenticacao:ChaveAssinatura" (base64, ≥ 32 bytes)
/// — variável de ambiente, user-secrets ou cofre; NUNCA do repositório. Em Development, sem
/// chave configurada, gera-se uma aleatória por execução (tokens morrem a cada reinício).
/// </summary>
public static class Autenticacao
{
    public const string Emissor = "hub-pedidos";
    public const string Audiencia = "hub-pedidos-clientes";
    public const string PoliticaOperador = "Operador";

    public static IServiceCollection AddAutenticacaoHub(this IServiceCollection services, IConfiguration configuration, IHostEnvironment ambiente)
    {
        var chave = ObterChave(configuration, ambiente);
        services.AddSingleton(new EmissorTokens(chave));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false; // mantém "sub", "role", "cliente_id" como vieram
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = Emissor,
                ValidAudience = Audiencia,
                IssuerSigningKey = chave,
                NameClaimType = "sub",
                RoleClaimType = "role",
                ClockSkew = TimeSpan.FromSeconds(30),
            };
            o.Events = new JwtBearerEvents
            {
                // WebSocket/SSE do navegador não enviam cabeçalho Authorization: o SignalR
                // manda o token na query "access_token" — aceito SÓ na rota do hub.
                OnMessageReceived = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase) && ctx.Request.Query["access_token"] is { Count: > 0 } token)
                    {
                        ctx.Token = token;
                    }

                    return Task.CompletedTask;
                },
            };
        });

        services.AddAuthorizationBuilder().AddPolicy(PoliticaOperador, p => p.RequireRole(Papeis.Operador));
        return services;
    }

    private static SymmetricSecurityKey ObterChave(IConfiguration configuration, IHostEnvironment ambiente)
    {
        var configurada = configuration["Autenticacao:ChaveAssinatura"];
        if (!string.IsNullOrWhiteSpace(configurada))
        {
            var bytes = Convert.FromBase64String(configurada);
            return bytes.Length >= 32 ? new SymmetricSecurityKey(bytes) : throw new InvalidOperationException("Autenticacao:ChaveAssinatura precisa de pelo menos 32 bytes.");
        }

        return ambiente.IsDevelopment() || ambiente.IsEnvironment("Testing")
            ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32))
            : throw new InvalidOperationException("Configure Autenticacao:ChaveAssinatura (fora do repositório).");
    }
}

/// <summary>Emite tokens com a mesma chave da validação (usado pelos testes e pela rota de desenvolvimento).</summary>
public sealed class EmissorTokens(SymmetricSecurityKey chave)
{
    private readonly JsonWebTokenHandler _handler = new();

    public string Emitir(string? clienteId, bool operador = false, TimeSpan? validade = null)
    {
        var claims = new List<Claim> { new("sub", clienteId ?? "operador") };
        if (clienteId is not null)
        {
            claims.Add(new Claim(Papeis.ClaimCliente, clienteId));
        }

        if (operador)
        {
            claims.Add(new Claim("role", Papeis.Operador));
        }

        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Autenticacao.Emissor,
            Audience = Autenticacao.Audiencia,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.Add(validade ?? TimeSpan.FromHours(1)),
            SigningCredentials = new SigningCredentials(chave, SecurityAlgorithms.HmacSha256),
        });
    }
}
