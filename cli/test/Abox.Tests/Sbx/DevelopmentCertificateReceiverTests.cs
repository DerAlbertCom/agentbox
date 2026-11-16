using System.Security.Cryptography;
using System.Text;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class DevelopmentCertificateReceiverTests
{
    [Theory]
    [InlineData("duplicate-password")]
    [InlineData("duplicate-version")]
    [InlineData("boolean-version")]
    [InlineData("unknown-property")]
    [InlineData("empty-pfx")]
    [InlineData("invalid-base64")]
    [InlineData("nul-password")]
    [InlineData("trailing-byte")]
    [InlineData("digest")]
    [InlineData("size")]
    public async Task MalformedPrivateInputFailsBeforeRuntimeAccessWithoutDiagnostics(string fault)
    {
        // Arrange: execute the shipped receiver, not a copy or a private helper.
        if (!OperatingSystem.IsLinux() || Environment.UserName == "root")
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value
                    + "The private receiver requires non-root Linux and Python 3."
            );
        var captured = await CaptureStageRequest();
        var arguments = captured.Arguments.SkipWhile(value => value != "python3").ToArray();

        // Act: every input is invalid before open_root; no runtime or host trust is touched.
        var result = await new ProcessRunner().RunAsync(
            new ProcessRequest(
                arguments[0],
                arguments[1..],
                StandardInput: MalformedFrame(fault),
                OutputPolicy: ProcessOutputPolicy.CapturedSensitive
            ),
            CancellationToken.None
        );

        // Assert
        result.ExitCode.Should().Be(1);
        result.StandardOutput.Should().BeEmpty();
        result.StandardError.Should().BeEmpty();
    }

    private static async Task<ProcessRequest> CaptureStageRequest()
    {
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        ProcessRequest? captured = null;
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<ProcessRequest>();
                return new ProcessResult(0, "", "");
            });
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));
        await client.StageDevelopmentCertificateAsync(
            "synthetic",
            new byte[] { 1 },
            "synthetic-private-password",
            CancellationToken.None
        );
        return captured!;
    }

    private static string MalformedFrame(string fault)
    {
        const string valid =
            "\"version\":1,\"pfx\":\"AQ==\",\"password\":\"synthetic-private-password\"";
        var json =
            "{"
            + (
                fault switch
                {
                    "duplicate-password" => valid + ",\"password\":\"other-private-password\"",
                    "duplicate-version" => valid + ",\"version\":1",
                    "boolean-version" => valid.Replace("\"version\":1", "\"version\":true"),
                    "unknown-property" => valid + ",\"extra\":1",
                    "empty-pfx" => valid.Replace("AQ==", ""),
                    "invalid-base64" => valid.Replace("AQ==", "!not-base64!"),
                    "nul-password" => valid.Replace(
                        "synthetic-private-password",
                        "private\\u0000password"
                    ),
                    _ => valid,
                }
            )
            + "}";
        var bytes = Encoding.UTF8.GetBytes(json);
        var frame =
            $"ABOX-CERTIFICATE-1\n{bytes.Length}\n{Convert.ToHexStringLower(SHA256.HashData(bytes))}\n{json}";
        return fault switch
        {
            "trailing-byte" => frame + "x",
            "digest" => frame.Replace(
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                new string('0', 64)
            ),
            "size" => "ABOX-CERTIFICATE-1\n1500001\n",
            _ => frame,
        };
    }
}
