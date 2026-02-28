using System.IO;
using FluentAssertions;
using WriteSpeech.Core.Services.IDE;

namespace WriteSpeech.Tests.Services;

public class SourceFileParserTests
{
    [Fact]
    public void ExtractIdentifiersFromContent_CSharpCode_ExtractsIdentifiers()
    {
        var code = """
            public class UserAuthenticationService
            {
                private readonly ILogger _logger;

                public async Task<bool> ValidateCredentials(string username, string password)
                {
                    var hashedPassword = HashPassword(password);
                    return await _repository.CheckCredentials(username, hashedPassword);
                }
            }
            """;

        var identifiers = SourceFileParser.ExtractIdentifiersFromContent(code);

        identifiers.Should().Contain("UserAuthenticationService");
        identifiers.Should().Contain("ValidateCredentials");
        identifiers.Should().Contain("HashPassword");
        identifiers.Should().Contain("CheckCredentials");
        identifiers.Should().Contain("ILogger");
        identifiers.Should().Contain("_logger");
        identifiers.Should().Contain("_repository");
        identifiers.Should().Contain("hashedPassword");
    }

    [Fact]
    public void ExtractIdentifiersFromContent_FiltersCommonKeywords()
    {
        var code = """
            public static void Main(string[] args)
            {
                var result = new List<int>();
                if (true) return;
                for (int i = 0; i < 10; i++) { }
            }
            """;

        var identifiers = SourceFileParser.ExtractIdentifiersFromContent(code);

        identifiers.Should().NotContain("public");
        identifiers.Should().NotContain("static");
        identifiers.Should().NotContain("void");
        identifiers.Should().NotContain("string");
        identifiers.Should().NotContain("var");
        identifiers.Should().NotContain("new");
        identifiers.Should().NotContain("true");
        identifiers.Should().NotContain("return");
        identifiers.Should().NotContain("for");
        identifiers.Should().NotContain("int");
    }

    [Fact]
    public void ExtractIdentifiersFromContent_FiltersShortLowercaseWords()
    {
        var code = """
            var name = "test";
            var data = GetData();
            var ApiClient = CreateClient();
            """;

        var identifiers = SourceFileParser.ExtractIdentifiersFromContent(code);

        // Short all-lowercase words should be filtered
        identifiers.Should().NotContain("name");
        identifiers.Should().NotContain("data");
        identifiers.Should().NotContain("test");

        // PascalCase and camelCase are kept
        identifiers.Should().Contain("GetData");
        identifiers.Should().Contain("ApiClient");
        identifiers.Should().Contain("CreateClient");
    }

    [Fact]
    public void ExtractIdentifiersFromContent_TypeScriptCode_ExtractsIdentifiers()
    {
        var code = """
            interface UserProfile {
                displayName: string;
                avatarUrl: string;
            }

            const fetchUserProfile = async (userId: string): Promise<UserProfile> => {
                const response = await apiClient.get(`/users/${userId}`);
                return response.data;
            };
            """;

        var identifiers = SourceFileParser.ExtractIdentifiersFromContent(code);

        identifiers.Should().Contain("UserProfile");
        identifiers.Should().Contain("displayName");
        identifiers.Should().Contain("avatarUrl");
        identifiers.Should().Contain("fetchUserProfile");
        identifiers.Should().Contain("apiClient");
        identifiers.Should().Contain("userId");
    }

    [Fact]
    public void ExtractIdentifiersFromContent_EmptyContent_ReturnsEmpty()
    {
        SourceFileParser.ExtractIdentifiersFromContent("").Should().BeEmpty();
    }

    [Fact]
    public void ExtractIdentifiers_WithTempDirectory_ExtractsFromFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"WriteSpeechTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Service.cs"),
                "public class MySpecialService { public void DoWork() {} }");
            File.WriteAllText(Path.Combine(tempDir, "Component.tsx"),
                "const MyComponent = () => { const handleClick = () => {}; };");

            var identifiers = SourceFileParser.ExtractIdentifiers(tempDir);

            identifiers.Should().Contain("MySpecialService");
            identifiers.Should().Contain("DoWork");
            identifiers.Should().Contain("MyComponent");
            identifiers.Should().Contain("handleClick");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ExtractIdentifiers_SkipsExcludedDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"WriteSpeechTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Source file in root
            File.WriteAllText(Path.Combine(tempDir, "App.cs"),
                "public class AppEntryPoint {}");

            // Source file in node_modules (should be excluded)
            var nodeModules = Path.Combine(tempDir, "node_modules", "pkg");
            Directory.CreateDirectory(nodeModules);
            File.WriteAllText(Path.Combine(nodeModules, "index.js"),
                "const ExcludedPackageClass = {};");

            // Source file in .git (should be excluded)
            var gitDir = Path.Combine(tempDir, ".git", "hooks");
            Directory.CreateDirectory(gitDir);
            File.WriteAllText(Path.Combine(gitDir, "pre-commit.py"),
                "def HiddenGitHookFunction(): pass");

            var identifiers = SourceFileParser.ExtractIdentifiers(tempDir);

            identifiers.Should().Contain("AppEntryPoint");
            identifiers.Should().NotContain("ExcludedPackageClass");
            identifiers.Should().NotContain("HiddenGitHookFunction");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ExtractIdentifiers_RespectsMaxResults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"WriteSpeechTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write many unique identifiers
            var lines = Enumerable.Range(0, 50)
                .Select(i => $"public class GeneratedClass{i:D3} {{}}")
                .ToList();
            File.WriteAllText(Path.Combine(tempDir, "Generated.cs"), string.Join("\n", lines));

            var identifiers = SourceFileParser.ExtractIdentifiers(tempDir, maxResults: 10);

            identifiers.Should().HaveCountLessThanOrEqualTo(10);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CollectFileNames_WithTempDirectory_CollectsSourceFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"WriteSpeechTest_{Guid.NewGuid():N}");
        var subDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(subDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "App.cs"), "");
            File.WriteAllText(Path.Combine(subDir, "Component.tsx"), "");
            File.WriteAllText(Path.Combine(tempDir, "readme.md"), ""); // Not a source file
            File.WriteAllText(Path.Combine(tempDir, "data.json"), ""); // Not a source file

            var files = SourceFileParser.CollectFileNames(tempDir);

            files.Should().Contain("App.cs");
            files.Should().Contain("Component.tsx");
            files.Should().NotContain("readme.md");
            files.Should().NotContain("data.json");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CollectFileNames_ExcludesNodeModules()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"WriteSpeechTest_{Guid.NewGuid():N}");
        var nodeModules = Path.Combine(tempDir, "node_modules", "pkg");
        Directory.CreateDirectory(nodeModules);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "index.ts"), "");
            File.WriteAllText(Path.Combine(nodeModules, "lib.js"), "");

            var files = SourceFileParser.CollectFileNames(tempDir);

            files.Should().Contain("index.ts");
            files.Should().NotContain("lib.js");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CollectFileNames_RespectsMaxResults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"WriteSpeechTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            for (int i = 0; i < 20; i++)
                File.WriteAllText(Path.Combine(tempDir, $"File{i:D2}.cs"), "");

            var files = SourceFileParser.CollectFileNames(tempDir, maxResults: 5);

            files.Should().HaveCountLessThanOrEqualTo(5);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- M1: Sensitive identifier deny-list ---

    [Theory]
    [InlineData("apiKey")]
    [InlineData("ApiKey")]
    [InlineData("APIKEY")]
    [InlineData("api_key")]
    [InlineData("secret")]
    [InlineData("secretKey")]
    [InlineData("password")]
    [InlineData("token")]
    [InlineData("accesstoken")]
    [InlineData("access_token")]
    [InlineData("credential")]
    [InlineData("privatekey")]
    [InlineData("private_key")]
    [InlineData("connectionstring")]
    [InlineData("client_secret")]
    [InlineData("masterkey")]
    [InlineData("jwtsecret")]
    [InlineData("jwt_secret")]
    [InlineData("jwttoken")]
    [InlineData("jwt_token")]
    [InlineData("oauthtoken")]
    [InlineData("oauth_token")]
    [InlineData("oauthsecret")]
    [InlineData("oauth_secret")]
    [InlineData("github_token")]
    [InlineData("githubtoken")]
    [InlineData("gitlab_token")]
    [InlineData("gitlabtoken")]
    [InlineData("aws_access_key")]
    [InlineData("aws_secret_key")]
    [InlineData("awsaccesskey")]
    [InlineData("awssecretkey")]
    [InlineData("certificate_key")]
    [InlineData("certificatekey")]
    [InlineData("database_password")]
    [InlineData("databasepassword")]
    [InlineData("dbpassword")]
    [InlineData("db_password")]
    [InlineData("smtp_password")]
    [InlineData("smtppassword")]
    public void IsSensitiveIdentifier_ReturnsTrueForSensitiveNames(string identifier)
    {
        SourceFileParser.IsSensitiveIdentifier(identifier).Should().BeTrue();
    }

    [Theory]
    [InlineData("UserService")]
    [InlineData("GetData")]
    [InlineData("Configuration")]
    [InlineData("HttpClient")]
    [InlineData("TokenType")]
    public void IsSensitiveIdentifier_ReturnsFalseForNormalNames(string identifier)
    {
        SourceFileParser.IsSensitiveIdentifier(identifier).Should().BeFalse();
    }

    [Fact]
    public void ExtractIdentifiersFromContent_FiltersSensitiveIdentifiers()
    {
        var code = """
            public class AuthService
            {
                private string apiKey;
                private string secretKey;
                private string connectionString;
                public string UserName { get; set; }
            }
            """;

        var identifiers = SourceFileParser.ExtractIdentifiersFromContent(code);

        identifiers.Should().Contain("AuthService");
        identifiers.Should().Contain("UserName");
        identifiers.Should().NotContain("apiKey");
        identifiers.Should().NotContain("secretKey");
        identifiers.Should().NotContain("connectionString");
    }

    [Fact]
    public void CollectFileNames_ReturnsSortedResults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"WriteSpeechTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Zebra.cs"), "");
            File.WriteAllText(Path.Combine(tempDir, "Alpha.cs"), "");
            File.WriteAllText(Path.Combine(tempDir, "Middle.cs"), "");

            var files = SourceFileParser.CollectFileNames(tempDir);

            files.Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
