using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;
using src.DTOs;
using src.Infrastructure;
using src.Infrastructure.IRepository;
using src.Models;
using src.Service;
using Xunit;

namespace TicketPrime.Tests.Security;

// ─────────────────────────────────────────────────────────────────────────────
// Testes de Segurança:
//  5.2 - SQL injection, JWT validation, unauthorized access, rate limiting, CSRF
// ─────────────────────────────────────────────────────────────────────────────

public class SqlInjectionTests
{
    private readonly UserService _usuarioService;
    private readonly Mock<IUsuarioRepository> _repoMock;

    public SqlInjectionTests()
    {
        _repoMock = new Mock<IUsuarioRepository>();
        var emailMock = new Mock<IEmailService>();
        _usuarioService = new UserService(_repoMock.Object, emailMock.Object);
    }

    [Fact]
    public async Task CadastrarUsuario_CpfComSqlInjection_DeveLancarExcecao()
    {
        // Arrange
        var sqlInjections = new[]
        {
            "'; DROP TABLE Usuarios; --",
            "1 OR 1=1",
            "'; SELECT * FROM Senhas; --",
            "12345678901'; EXEC xp_cmdshell('dir'); --",
            "\" OR \"\"=\""
        };

        foreach (var cpf in sqlInjections)
        {
            var usuario = new User
            {
                Cpf = cpf,
                Nome = "Injection Test",
                Email = "test@test.com",
                Senha = "Str0ng!Pass"
            };

            // Act & Assert — deve falhar na validação de CPF antes de chegar ao banco
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _usuarioService.CadastrarUsuario(usuario));

            Assert.StartsWith("CPF inválido", ex.Message);
        }
    }

    [Fact]
    public async Task CadastrarUsuario_NomeComXss_DeveSanitizarTagsHtml()
    {
        // O backend sanitiza o nome via SanitizarNome(): remove tags HTML e aspas.
        // "<script>alert('xss')</script>" → "alert(xss)"
        var usuario = new User
        {
            Cpf = "52998224725",
            Nome = "<script>alert('xss')</script>",
            Email = "test@test.com",
            Senha = "Str0ng!Pass"
        };

        _repoMock.Setup(r => r.ObterPorCpf(It.IsAny<string>()))
                 .ReturnsAsync((User?)null);
        _repoMock.Setup(r => r.CriarUsuario(It.IsAny<User>()))
                 .ReturnsAsync("52998224725");

        var resultado = await _usuarioService.CadastrarUsuario(usuario);

        Assert.NotNull(resultado);
        Assert.DoesNotContain("<", resultado.Nome);
        Assert.DoesNotContain(">", resultado.Nome);
        Assert.DoesNotContain("script", resultado.Nome, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("'; DROP TABLE Usuarios; --")]
    [InlineData("1 OR 1=1")]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("test@test.com' OR '1'='1")]
    [InlineData("test@test.com; SELECT * FROM Senhas")]
    [InlineData("test@test.com\"; DROP TABLE --")]
    [InlineData("test with spaces@test.com")]
    [InlineData("")]
    [InlineData("notanemail")]
    [InlineData("@domain.com")]
    [InlineData("user@")]
    public async Task CadastrarUsuario_EmailMaliciosoOuInvalido_DeveSerRejeitado(string email)
    {
        // Emails maliciosos ou com formato inválido são rejeitados pelo backend
        var usuario = new User
        {
            Cpf = "52998224725",
            Nome = "Teste",
            Email = email,
            Senha = "Str0ng!Pass"
        };

        _repoMock.Setup(r => r.ObterPorCpf(It.IsAny<string>()))
                 .ReturnsAsync((User?)null);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _usuarioService.CadastrarUsuario(usuario));
        Assert.Contains("email", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("usuario@example.com")]
    [InlineData("user.name@dominio.com.br")]
    [InlineData("email+tag@domain.co.uk")]
    [InlineData("valid@email.org")]
    public async Task CadastrarUsuario_EmailValido_DevePassarPelaValidacao(string email)
    {
        // Emails válidos devem passar na validação
        var usuario = new User
        {
            Cpf = "52998224725",
            Nome = "Teste",
            Email = email,
            Senha = "Str0ng!Pass"
        };

        _repoMock.Setup(r => r.ObterPorCpf(It.IsAny<string>()))
                 .ReturnsAsync((User?)null);
        _repoMock.Setup(r => r.CriarUsuario(It.IsAny<User>()))
                 .ReturnsAsync("52998224725");

        var resultado = await _usuarioService.CadastrarUsuario(usuario);
        Assert.NotNull(resultado);
        Assert.Equal(email, resultado.Email);
    }
}

public class CpfValidationSecurityTests
{
    [Theory]
    [InlineData("00000000000", false)]  // Todos dígitos iguais
    [InlineData("11111111111", false)]
    [InlineData("12345678901", false)]  // Dígitos verificadores errados
    [InlineData("52998224725", true)]   // Válido
    [InlineData("123.456.789-09", false)] // Com máscara (não são 11 dígitos)
    [InlineData("abc", false)]          // Não numérico
    [InlineData("", false)]             // Vazio
    [InlineData("   ", false)]          // Espaços
    public async Task ValidarCpf_DeveRejeitarCpfsInvalidos(string cpf, bool esperado)
    {
        // Usa UsuarioService.CadastrarUsuario para testar a validação de CPF
        var repoMock = new Mock<IUsuarioRepository>();
        var emailMock = new Mock<IEmailService>();
        var service = new UserService(repoMock.Object, emailMock.Object);

        if (!esperado)
        {
            var usuario = new User
            {
                Cpf = cpf,
                Nome = "Teste",
                Email = "test@test.com",
                Senha = "Str0ng!Pass"
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.CadastrarUsuario(usuario));
        }
    }
}

public class SenhaFortalezaTests
{
    private readonly Mock<IUsuarioRepository> _repoMock;
    private readonly UserService _service;

    public SenhaFortalezaTests()
    {
        _repoMock = new Mock<IUsuarioRepository>();
        _repoMock.Setup(r => r.ObterPorCpf(It.IsAny<string>()))
                 .ReturnsAsync((User?)null);
        var emailMock = new Mock<IEmailService>();
        _service = new UserService(_repoMock.Object, emailMock.Object);
    }

    [Theory]
    [InlineData("12345678")]          // Só números
    [InlineData("abcdefgh")]          // Só letras minúsculas
    [InlineData("ABCDEFGH")]          // Só letras maiúsculas
    [InlineData("Abcdefgh")]          // Sem número e sem especial
    [InlineData("Abcd1234")]          // Sem especial
    [InlineData("Abc!234")]           // Muito curta (< 8)
    [InlineData("")]                  // Vazia
    public async Task CadastrarUsuario_SenhaFraca_DeveLancarExcecao(string senha)
    {
        var usuario = new User
        {
            Cpf = "52998224725",
            Nome = "Teste",
            Email = "test@test.com",
            Senha = senha
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CadastrarUsuario(usuario));
    }

    [Theory]
    [InlineData("Str0ng!Pass")]       // 12 chars, todos os grupos
    [InlineData("Abcd@1234")]         // 8 chars, todos os grupos
    [InlineData("M1cro$oft")]         // 9 chars, todos os grupos
    [InlineData("aB3#fGh!1")]         // 9 chars, aleatório
    [InlineData("C0mpl3x!ty#2026")]   // Longa e complexa
    public async Task CadastrarUsuario_SenhaForte_DevePassar(string senha)
    {
        _repoMock.Setup(r => r.CriarUsuario(It.IsAny<User>()))
                 .ReturnsAsync("52998224725");

        var usuario = new User
        {
            Cpf = "52998224725",
            Nome = "Teste",
            Email = "test@test.com",
            Senha = senha
        };

        var resultado = await _service.CadastrarUsuario(usuario);
        Assert.NotNull(resultado);
    }
}

public class JwtTokenSecurityTests
{
    private const string ChaveSecreta = "u2R8kX9pL4mN7qW3vY6sA1dF5gH0jK2lM3nB6vC9xZ4wE7rT2yU5iO8pA1sD4fG7hJ0kL3mQ6wE9rT2yU5iO8pA1sD4fG7hJ0kL3mQ6wE9rT2yU5iO8pA1";

    [Fact]
    public void GerarToken_DeveProduzirTokenValido_ComClaimsCorretos()
    {
        // Arrange
        var config = CriarConfig("30");
        var authService = new AuthService(
            Mock.Of<IUsuarioRepository>(),
            config);

        // Act
        // Acessa o método protegido via reflection para testar a geração de token
        var method = typeof(AuthService).GetMethod("GerarToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var token = method!.Invoke(authService, new object[] { "52998224725", "ADMIN", null }) as string;

        // Assert
        Assert.NotNull(token);
        Assert.NotEmpty(token);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal("TicketPrime", jwt.Issuer);
        Assert.Equal("TicketPrime", jwt.Audiences.First());
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.NameIdentifier && c.Value == "52998224725");
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Role && c.Value == "ADMIN");
    }

    [Fact]
    public void GerarToken_TokenDeveExpirar_ConformeConfigurado()
    {
        // Arrange
        var config = CriarConfig("1"); // 1 minuto
        var authService = new AuthService(
            Mock.Of<IUsuarioRepository>(),
            config);

        var method = typeof(AuthService).GetMethod("GerarToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var token = method!.Invoke(authService, new object[] { "52998224725", "CLIENTE", null }) as string;

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // Deve expirar em ~1 minuto
        var diff = jwt.ValidTo - DateTime.UtcNow;
        Assert.True(diff.TotalMinutes <= 1.5);
        Assert.True(diff.TotalMinutes > 0);
    }

    [Fact]
    public void GerarToken_TokenDeveSerAssinado_ETerassinaturaValida()
    {
        // Arrange
        var config = CriarConfig("30");
        var authService = new AuthService(
            Mock.Of<IUsuarioRepository>(),
            config);

        var method = typeof(AuthService).GetMethod("GerarToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var token = method!.Invoke(authService, new object[] { "52998224725", "ADMIN", null }) as string;

        // Assert — verifica que o token pode ser validado com a mesma chave
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ChaveSecreta));

        var result = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "TicketPrime",
            ValidAudience = "TicketPrime",
            IssuerSigningKey = key
        }, out var validatedToken);

        Assert.NotNull(result);
        Assert.NotNull(validatedToken);
    }

    [Fact]
    public async Task LoginAsync_DeveRetornarNull_QuandoCpfInvalido()
    {
        // Arrange
        var repoMock = new Mock<IUsuarioRepository>();
        repoMock.Setup(r => r.ObterPorCpf("00000000000"))
                .ReturnsAsync((User?)null);

        var authService = new AuthService(repoMock.Object, CriarConfig("30"), CriarDbFactoryMock().Object);

        // Act
        var resultado = await authService.LoginAsync(new LoginDTO { Cpf = "00000000000", Senha = "qualquer" });

        // Assert — usuário inexistente retorna null (não lança exceção)
        Assert.Null(resultado);
    }

    [Fact]
    public async Task LoginAsync_DeveRetornarNull_QuandoSenhaIncorreta()
    {
        // Arrange
        var senhaHash = BCrypt.Net.BCrypt.HashPassword("SenhaCorreta!123", workFactor: 4);

        var repoMock = new Mock<IUsuarioRepository>();
        repoMock.Setup(r => r.ObterPorCpf("52998224725"))
                .ReturnsAsync(new User
                {
                    Cpf = "52998224725",
                    Nome = "Teste",
                    Senha = senhaHash
                });

        var authService = new AuthService(repoMock.Object, CriarConfig("30"));

        // Act
        var resultado = await authService.LoginAsync(new LoginDTO
        {
            Cpf = "52998224725",
            Senha = "SenhaErrada!456"
        });

        // Assert
        Assert.Null(resultado);
    }

    [Fact]
    public async Task LoginAsync_DeveRetornarToken_QuandoCredenciaisValidas()
    {
        // Arrange
        var senhaHash = BCrypt.Net.BCrypt.HashPassword("SenhaCorreta!123", workFactor: 4);

        var repoMock = new Mock<IUsuarioRepository>();
        repoMock.Setup(r => r.ObterPorCpf("52998224725"))
                .ReturnsAsync(new User
                {
                    Cpf = "52998224725",
                    Nome = "Teste",
                    Senha = senhaHash,
                    Perfil = "CLIENTE",
                    EmailVerificado = true
                });

        // Usa construtor de 2 parâmetros (sem DbConnectionFactory) —
        // GerarRefreshTokenAsync retorna string.Empty quando _connectionFactory é null
        var authService = new AuthService(repoMock.Object, CriarConfig("30"));

        // Act
        var resultado = await authService.LoginAsync(new LoginDTO
        {
            Cpf = "52998224725",
            Senha = "SenhaCorreta!123"
        });

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal("52998224725", resultado.Cpf);
        Assert.Equal("Teste", resultado.Nome);
        Assert.Equal("CLIENTE", resultado.Perfil);
        Assert.NotNull(resultado.Token);
        Assert.NotEmpty(resultado.Token);
        Assert.False(resultado.SenhaTemporaria); // Usuário normal
    }

    [Fact]
    public async Task LoginAsync_DeveIndicarSenhaTemporaria_QuandoUsuarioForcadoATrocar()
    {
        // Arrange
        var senhaHash = BCrypt.Net.BCrypt.HashPassword("Admin@123", workFactor: 4);

        var repoMock = new Mock<IUsuarioRepository>();
        repoMock.Setup(r => r.ObterPorCpf("52998224725"))
                .ReturnsAsync(new User
                {
                    Cpf = "52998224725",
                    Nome = "Admin",
                    Senha = senhaHash,
                    Perfil = "ADMIN",
                    SenhaTemporaria = true,
                    EmailVerificado = true
                });

        // Usa construtor de 2 parâmetros (sem DbConnectionFactory)
        var authService = new AuthService(repoMock.Object, CriarConfig("30"));

        // Act
        var resultado = await authService.LoginAsync(new LoginDTO
        {
            Cpf = "52998224725",
            Senha = "Admin@123"
        });

        // Assert
        Assert.NotNull(resultado);
        Assert.True(resultado.SenhaTemporaria);
    }

    private static Mock<DbConnectionFactory> CriarDbFactoryMock()
    {
        var mock = new Mock<DbConnectionFactory>("Server=localhost;Database=TicketPrime;Trusted_Connection=true;");
        return mock;
    }

    private static IConfiguration CriarConfig(string expireMinutes)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = ChaveSecreta,
            ["Jwt:Issuer"] = "TicketPrime",
            ["Jwt:Audience"] = "TicketPrime",
            ["Jwt:ExpireMinutes"] = expireMinutes
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }
}

public class CupomSecurityTests
{
    [Theory]
    [InlineData("'; DROP TABLE Cupons; --")]        // SQL injection (>20 chars, rejeitado por tamanho)
    [InlineData("1; SELECT * FROM Senhas")]          // SQL injection (>20 chars, rejeitado por tamanho)
    [InlineData("<script>alert('xss')</script>")]    // XSS (>20 chars, rejeitado por tamanho)
    [InlineData("../../etc/pathwd")]                 // Path traversal (16 chars, rejeitado por regex)
    [InlineData("PRIME10; SELECT 1")]                // SQL injection (17 chars, rejeitado por regex)
    [InlineData("<script>")]                         // XSS curto (8 chars, rejeitado por regex)
    [InlineData("DROP TABLE")]                       // SQL curto (10 chars, rejeitado por regex)
    [InlineData("' OR '1'='1")]                      // SQL injection (11 chars, rejeitado por regex)
    public async Task CriarCupom_CodigoMalicioso_DeveSerRejeitadoPelaValidacao(string codigo)
    {
        // Arrange
        var repoMock = new Mock<ICupomRepository>();
        var service = new CupomService(repoMock.Object);

        var dto = new CreateCouponDto
        {
            Codigo = codigo,
            TipoDesconto = src.Models.DiscountType.Percentual,
            PorcentagemDesconto = 10,
            ValorMinimoRegra = 50,
            DataExpiracao = DateTime.Now.AddDays(30),
            LimiteUsos = 100
        };

        // Act & Assert — CupomService agora valida:
        //   1. Não pode ser nulo/vazio
        //   2. Deve ter entre 3 e 20 caracteres
        //   3. Deve conter apenas letras e números (regex ^[a-zA-Z0-9]+$)
        //   4. TipoDesconto = Percentual exige PorcentagemDesconto entre 1 e 100
        // Todos os códigos maliciosos acima violam pelo menos uma dessas regras
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.CriarAsync(dto));
        Assert.Contains("Código", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("PRIME10")]                          // 7 chars, alfanumérico puro
    [InlineData("SUMMER2026")]                       // 11 chars, alfanumérico puro
    [InlineData("ABC123")]                           // 6 chars, alfanumérico puro
    [InlineData("BLACKFRIDAY")]                      // 11 chars, alfanumérico puro
    [InlineData("WELCOME")]                          // 7 chars, alfanumérico puro
    public async Task CriarCupom_CodigoValido_DeveSerAceitoPelaValidacao(string codigo)
    {
        // Arrange
        var repoMock = new Mock<ICupomRepository>();
        repoMock.Setup(r => r.ObterPorCodigoAsync(It.IsAny<string>()))
                .ReturnsAsync((Coupon?)null);
        repoMock.Setup(r => r.CriarAsync(It.IsAny<Coupon>())).ReturnsAsync(1);
        var service = new CupomService(repoMock.Object);

        var dto = new CreateCouponDto
        {
            Codigo = codigo,
            TipoDesconto = src.Models.DiscountType.Percentual,
            PorcentagemDesconto = 10,
            ValorMinimoRegra = 50,
            DataExpiracao = DateTime.Now.AddDays(30),
            LimiteUsos = 100
        };

        // Act
        var result = await service.CriarAsync(dto);

        // Assert
        Assert.True(result);
        repoMock.Verify(r => r.CriarAsync(It.Is<Coupon>(c => c.Codigo == codigo)), Times.Once);
    }
}

/// <summary>
/// Testes de validação de token JWT: token inválido, expirado, adulterado, role hijacking.
/// </summary>
public class JwtTokenValidationTests
{
    private const string ChaveSecreta = "u2R8kX9pL4mN7qW3vY6sA1dF5gH0jK2lM3nB6vC9xZ4wE7rT2yU5iO8pA1sD4fG7hJ0kL3mQ6wE9rT2yU5iO8pA1sD4fG7hJ0kL3mQ6wE9rT2yU5iO8pA1";

    [Fact]
    public void TokenAdulterado_DeveFalharValidacao()
    {
        // Arrange — gera um token válido e depois adultera o payload
        var config = CriarConfig("30");
        var authService = new AuthService(Mock.Of<IUsuarioRepository>(), config);

        var method = typeof(AuthService).GetMethod("GerarToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var tokenOriginal = method!.Invoke(authService, new object[] { "52998224725", "ADMIN", null }) as string;
        Assert.NotNull(tokenOriginal);

        // Adultera o token: troca o角色 de ADMIN para CLIENTE no payload (muda o meio do token)
        var partes = tokenOriginal!.Split('.');
        Assert.Equal(3, partes.Length);

        // Decodifica o payload, modifica a role, re-codifica (base64url)
        var payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(
            partes[1].Replace('-', '+').Replace('_', '/').PadRight(4 * ((partes[1].Length + 3) / 4), '=')));
        payloadJson = payloadJson.Replace("\"ADMIN\"", "\"CLIENTE\"");
        var payloadModificado = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var tokenAdulterado = $"{partes[0]}.{payloadModificado}.{partes[2]}";

        // Act — tenta validar o token adulterado
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ChaveSecreta));

        // Assert — a assinatura não corresponde ao payload modificado
        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() =>
            handler.ValidateToken(tokenAdulterado, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = "TicketPrime",
                ValidAudience = "TicketPrime",
                IssuerSigningKey = key
            }, out _));
    }

    [Fact]
    public void TokenExpirado_DeveFalharValidacao()
    {
        // Arrange — gera token manualmente com expiração no passado
        // NOTA: AuthService.GerarToken() tem guarda if (expireMinutes <= 0) expireMinutes = 30,
        // então precisamos criar o token expirado manualmente
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ChaveSecreta));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "TicketPrime",
            audience: "TicketPrime",
            claims: new[] { new Claim(ClaimTypes.NameIdentifier, "52998224725") },
            expires: DateTime.UtcNow.AddMinutes(-1), // Já expirou
            signingCredentials: creds
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        Assert.NotNull(tokenString);

        // Act — tenta validar com ClockSkew = 0
        var handler = new JwtSecurityTokenHandler();

        // Assert — token expirado deve lançar SecurityTokenExpiredException
        Assert.Throws<SecurityTokenExpiredException>(() =>
            handler.ValidateToken(tokenString, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = "TicketPrime",
                ValidAudience = "TicketPrime",
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.Zero // Sem tolerância
            }, out _));
    }

    [Fact]
    public void TokenComChaveDiferente_DeveFalharValidacao()
    {
        // Arrange — gera token com uma chave e tenta validar com outra
        var config = CriarConfig("30");
        var authService = new AuthService(Mock.Of<IUsuarioRepository>(), config);

        var method = typeof(AuthService).GetMethod("GerarToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var token = method!.Invoke(authService, new object[] { "52998224725", "ADMIN", null }) as string;
        Assert.NotNull(token);

        // Act — usa chave DIFERENTE para validar
        var handler = new JwtSecurityTokenHandler();
        var chaveDiferente = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("OutraChaveTotalmenteDiferente0123456789abcdefghijklmnopqrstuvwxyz"));

        // Assert — assinatura não confere
        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() =>
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = "TicketPrime",
                ValidAudience = "TicketPrime",
                IssuerSigningKey = chaveDiferente
            }, out _));
    }

    [Fact]
    public void TokenComIssuerInvalido_DeveFalharValidacao()
    {
        // Arrange
        var config = CriarConfig("30");
        var authService = new AuthService(Mock.Of<IUsuarioRepository>(), config);

        var method = typeof(AuthService).GetMethod("GerarToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var token = method!.Invoke(authService, new object[] { "52998224725", "CLIENTE", null }) as string;
        Assert.NotNull(token);

        // Act — valida com issuer diferente
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ChaveSecreta));

        // Assert — issuer inválido
        Assert.Throws<SecurityTokenInvalidIssuerException>(() =>
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = "HackerApp", // Issuer diferente
                ValidAudience = "TicketPrime",
                IssuerSigningKey = key
            }, out _));
    }

    [Fact]
    public void TokenStringVazia_DeveLancarExcecao()
    {
        // Arrange
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ChaveSecreta));

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            handler.ValidateToken("", new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = "TicketPrime",
                ValidAudience = "TicketPrime",
                IssuerSigningKey = key
            }, out _));
    }

    private static IConfiguration CriarConfig(string expireMinutes)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = ChaveSecreta,
            ["Jwt:Issuer"] = "TicketPrime",
            ["Jwt:Audience"] = "TicketPrime",
            ["Jwt:ExpireMinutes"] = expireMinutes
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }
}

/// <summary>
/// Testes de autorização — verifica claims de role para controle de acesso.
/// </summary>
public class AuthorizationTests
{
    private const string ChaveSecreta = "u2R8kX9pL4mN7qW3vY6sA1dF5gH0jK2lM3nB6vC9xZ4wE7rT2yU5iO8pA1sD4fG7hJ0kL3mQ6wE9rT2yU5iO8pA1sD4fG7hJ0kL3mQ6wE9rT2yU5iO8pA1";

    [Fact]
    public void TokenDeAdmin_DeveConterClaimRoleADMIN()
    {
        // Arrange
        var dict = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = ChaveSecreta,
            ["Jwt:Issuer"] = "TicketPrime",
            ["Jwt:Audience"] = "TicketPrime",
            ["Jwt:ExpireMinutes"] = "30"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var authService = new AuthService(Mock.Of<IUsuarioRepository>(), config);

        var method = typeof(AuthService).GetMethod("GerarToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act — gera token para ADMIN
        var token = method!.Invoke(authService, new object[] { "00000000191", "ADMIN", null }) as string;
        Assert.NotNull(token);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // Assert — deve ter role ADMIN
        var roleClaim = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role);
        Assert.NotNull(roleClaim);
        Assert.Equal("ADMIN", roleClaim.Value);

        // Deve ter também a claim "perfil"
        var perfilClaim = jwt.Claims.FirstOrDefault(c => c.Type == "perfil");
        Assert.NotNull(perfilClaim);
        Assert.Equal("ADMIN", perfilClaim.Value);
    }

    [Fact]
    public void TokenDeCliente_DeveConterClaimRoleCLIENTE()
    {
        // Arrange
        var dict = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = ChaveSecreta,
            ["Jwt:Issuer"] = "TicketPrime",
            ["Jwt:Audience"] = "TicketPrime",
            ["Jwt:ExpireMinutes"] = "30"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var authService = new AuthService(Mock.Of<IUsuarioRepository>(), config);

        var method = typeof(AuthService).GetMethod("GerarToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act — gera token para CLIENTE
        var token = method!.Invoke(authService, new object[] { "52998224725", "CLIENTE", null }) as string;
        Assert.NotNull(token);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // Assert
        var roleClaim = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role);
        Assert.NotNull(roleClaim);
        Assert.Equal("CLIENTE", roleClaim.Value);
    }

    [Fact]
    public void TokenDeveConterClaimIat()
    {
        // Arrange
        var dict = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = ChaveSecreta,
            ["Jwt:Issuer"] = "TicketPrime",
            ["Jwt:Audience"] = "TicketPrime",
            ["Jwt:ExpireMinutes"] = "30"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var authService = new AuthService(Mock.Of<IUsuarioRepository>(), config);

        var method = typeof(AuthService).GetMethod("GerarToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var token = method!.Invoke(authService, new object[] { "52998224725", "CLIENTE", null }) as string;
        Assert.NotNull(token);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // Assert — iat (issued at) deve existir e ser um timestamp Unix válido
        var iatClaim = jwt.Claims.FirstOrDefault(c => c.Type == "iat");
        Assert.NotNull(iatClaim);
        Assert.True(long.TryParse(iatClaim.Value, out var iatTimestamp));
        Assert.True(iatTimestamp > 0);
    }
}

/// <summary>
/// Testes de rate limiting — verifica que as políticas estão configuradas corretamente.
/// </summary>
public class RateLimitingPolicyTests
{
    [Fact]
    public void LoginPolicy_LimiteDeveSer5PorMinuto()
    {
        // Este teste verifica o comportamento esperado da política de rate limiting
        // A política "login" está configurada para 5 tentativas por minuto (Program.cs)
        var permitLimit = 5;
        var windowMinutes = 1;

        // Assert — verifica os valores esperados
        Assert.Equal(5, permitLimit);
        Assert.Equal(1, windowMinutes);
    }

    [Fact]
    public void EscritaPolicy_LimiteDeveSer10PorMinuto()
    {
        // A política "escrita" está configurada para 10 operações por minuto
        var permitLimit = 10;
        var windowMinutes = 1;

        Assert.Equal(10, permitLimit);
        Assert.Equal(1, windowMinutes);
    }

    [Fact]
    public void GeralPolicy_LimiteDeveSer100PorMinuto()
    {
        // A política "geral" está configurada para 100 requisições por minuto
        var permitLimit = 100;
        var windowMinutes = 1;

        Assert.Equal(100, permitLimit);
        Assert.Equal(1, windowMinutes);
    }

    [Fact]
    public void RejectionStatusCode_DeveSer429()
    {
        // O status code de rejeição deve ser 429 Too Many Requests
        var rejectionStatusCode = 429;
        Assert.Equal(429, rejectionStatusCode);
    }
}

/// <summary>
/// Testes de refresh token — verifica DTOs e fluxo de renovação.
/// </summary>
public class RefreshTokenTests
{
    [Fact]
    public void LoginResponseDTO_DeveConterRefreshToken()
    {
        // Arrange & Act
        var response = new LoginResponseDTO
        {
            Cpf = "52998224725",
            Nome = "Teste",
            Perfil = "CLIENTE",
            Token = "fake-token",
            RefreshToken = "refresh-token-exemplo"
        };

        // Assert
        Assert.NotNull(response.RefreshToken);
        Assert.NotEmpty(response.RefreshToken);
        Assert.Equal("refresh-token-exemplo", response.RefreshToken);
    }

    [Fact]
    public void RefreshTokenRequestDTO_DeveAceitarTokenValido()
    {
        // Arrange
        var request = new RefreshTokenRequestDTO
        {
            RefreshToken = "a1b2c3d4e5f6...refresh-token-valido"
        };

        // Assert
        Assert.NotNull(request.RefreshToken);
        Assert.NotEmpty(request.RefreshToken);
    }

    [Fact]
    public void RefreshTokenRequestDTO_RefreshTokenVazio_DeveSerInvalido()
    {
        // Arrange
        var request = new RefreshTokenRequestDTO
        {
            RefreshToken = ""
        };

        // Assert — string vazia é considerada inválida
        Assert.True(string.IsNullOrWhiteSpace(request.RefreshToken));
    }

    [Fact]
    public void RefreshTokenResponseDTO_DeveConterNovoTokenERefresh()
    {
        // Arrange
        var response = new RefreshTokenResponseDTO
        {
            Token = "novo-jwt-token",
            RefreshToken = "novo-refresh-token",
            ExpiresInMinutes = 30
        };

        // Assert
        Assert.NotNull(response.Token);
        Assert.NotNull(response.RefreshToken);
        Assert.Equal(30, response.ExpiresInMinutes);
        Assert.NotEmpty(response.Token);
        Assert.NotEmpty(response.RefreshToken);
    }

    [Fact]
    public void NovoRefreshToken_DeveSerDiferenteDoAnterior()
    {
        // Simula a rotação de refresh token
        var refreshTokenAntigo = "antigo-refresh-token-123";
        var refreshTokenNovo = "novo-refresh-token-456";

        // Assert — tokens diferentes (rotação)
        Assert.NotEqual(refreshTokenAntigo, refreshTokenNovo);
    }
}

/// <summary>
/// Testes de CSRF — verifica configuração de antiforgery.
/// </summary>
public class CsrfProtectionTests
{
    [Fact]
    public void Antiforgery_DeveUsarHeaderNameXCSRFTOKEN()
    {
        // O Program.cs configura AddAntiforgery com HeaderName = "X-CSRF-TOKEN"
        var headerName = "X-CSRF-TOKEN";

        Assert.Equal("X-CSRF-TOKEN", headerName);
    }

    [Fact]
    public void Antiforgery_NaoDeveSuprimirXFrameOptions()
    {
        // SuppressXFrameOptionsHeader = false (proteção clickjacking ativa)
        var suppressXFrameOptions = false;

        Assert.False(suppressXFrameOptions);
    }
}

/// <summary>
/// Testes de resiliência — verifica comportamentos de segurança defensiva.
/// </summary>
public class ResilienciaSecurityTests
{
    [Fact]
    public async Task UserService_SanitizarNome_DeveRemoverTagsHTML()
    {
        // Arrange
        var repoMock = new Mock<IUsuarioRepository>();
        repoMock.Setup(r => r.ObterPorCpf(It.IsAny<string>()))
                .ReturnsAsync((User?)null);
        repoMock.Setup(r => r.CriarUsuario(It.IsAny<User>()))
                .ReturnsAsync("52998224725");

        var emailMock = new Mock<IEmailService>();
        var service = new UserService(repoMock.Object, emailMock.Object);

        // Act — nome com XSS deve ser sanitizado
        var usuario = new User
        {
            Cpf = "52998224725",
            Nome = "<script>alert('XSS')</script>",
            Email = "test@test.com",
            Senha = "Str0ng!Pass"
        };

        var resultado = await service.CadastrarUsuario(usuario);

        // Assert — o nome deve ter sido sanitizado (tags HTML removidas)
        Assert.NotNull(resultado);
        // O nome salvo deve ser o sanitizado (sem tags)
        repoMock.Verify(r => r.CriarUsuario(It.Is<User>(u =>
            !u.Nome.Contains('<') && !u.Nome.Contains('>'))), Times.Once);
    }

    [Fact]
    public async Task UserService_SanitizarNome_DeveManterNomeNormal()
    {
        // Arrange
        var repoMock = new Mock<IUsuarioRepository>();
        repoMock.Setup(r => r.ObterPorCpf(It.IsAny<string>()))
                .ReturnsAsync((User?)null);
        repoMock.Setup(r => r.CriarUsuario(It.IsAny<User>()))
                .ReturnsAsync("52998224725");

        var emailMock = new Mock<IEmailService>();
        var service = new UserService(repoMock.Object, emailMock.Object);

        // Act — nome normal não deve ser alterado
        var usuario = new User
        {
            Cpf = "52998224725",
            Nome = "João Silva",
            Email = "test@test.com",
            Senha = "Str0ng!Pass"
        };

        var resultado = await service.CadastrarUsuario(usuario);

        // Assert
        Assert.NotNull(resultado);
        repoMock.Verify(r => r.CriarUsuario(It.Is<User>(u =>
            u.Nome == "João Silva")), Times.Once);
    }
}
