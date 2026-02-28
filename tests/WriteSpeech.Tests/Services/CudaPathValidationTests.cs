using FluentAssertions;
using WriteSpeech.App;

namespace WriteSpeech.Tests.Services;

public class CudaPathValidationTests
{
    [Theory]
    [InlineData(@"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.1")]
    [InlineData(@"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.0")]
    [InlineData(@"C:\Program Files\NVIDIA Corporation\something")]
    public void IsValidCudaPath_TrustedPath_ReturnsTrue(string path)
    {
        WriteSpeech.App.App.IsValidCudaPath(path).Should().BeTrue();
    }

    [Theory]
    [InlineData(@"C:\Users\attacker\fake_cuda")]
    [InlineData(@"C:\tmp\cuda")]
    [InlineData(@"D:\NVIDIA GPU Computing Toolkit\CUDA")]
    [InlineData(@"C:\temp\evil")]
    public void IsValidCudaPath_UntrustedPath_ReturnsFalse(string path)
    {
        WriteSpeech.App.App.IsValidCudaPath(path).Should().BeFalse();
    }

    [Fact]
    public void IsValidCudaPath_TraversalAttempt_ReturnsFalse()
    {
        var path = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\..\..\..\..\tmp\evil";
        WriteSpeech.App.App.IsValidCudaPath(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidCudaPath_EmptyOrWhitespace_ReturnsFalse(string path)
    {
        WriteSpeech.App.App.IsValidCudaPath(path).Should().BeFalse();
    }
}
