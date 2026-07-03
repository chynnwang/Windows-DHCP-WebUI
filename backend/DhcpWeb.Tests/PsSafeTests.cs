using DhcpWeb.Api.Services;

namespace DhcpWeb.Tests;

public class PsSafeTests
{
    // ---------- IP ----------
    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("10.0.0.255")]
    [InlineData("255.255.255.0")]
    public void Ip_Valid_ReturnsCanonical(string v)
    {
        Assert.Equal(v, PsSafe.Ip(v));
    }

    [Theory]
    [InlineData("1.2.3.4;evil")]          // 命令注入尝试
    [InlineData("192.168.1.1' ; rm -rf")] // 引号+注入
    [InlineData("::1")]                    // IPv6 应拒绝
    [InlineData("999.1.1.1")]
    [InlineData("not-an-ip")]
    [InlineData("")]
    public void Ip_Invalid_Throws(string v)
    {
        Assert.Throws<ArgumentException>(() => PsSafe.Ip(v));
    }

    // ---------- MAC ----------
    [Theory]
    [InlineData("00-11-22-33-44-55", "001122334455")]
    [InlineData("00:11:22:33:44:55", "001122334455")]
    [InlineData("aabbccddeeff", "aabbccddeeff")]
    public void Mac_Valid_NormalizesToHex(string v, string expected)
    {
        Assert.Equal(expected, PsSafe.Mac(v));
    }

    [Theory]
    [InlineData("00-11-22-33-44")]         // 太短
    [InlineData("zz-11-22-33-44-55")]      // 非 hex
    [InlineData("00-11-22-33-44-55; calc")]
    public void Mac_Invalid_Throws(string v)
    {
        Assert.Throws<ArgumentException>(() => PsSafe.Mac(v));
    }

    // ---------- Name ----------
    [Theory]
    [InlineData("办公网段")]
    [InlineData("Office-1F_主楼")]
    [InlineData("scope 01")]
    public void Name_Valid_ReturnsSame(string v)
    {
        Assert.Equal(v, PsSafe.Name(v));
    }

    [Theory]
    [InlineData("evil'; Remove-Item C:\\ -Recurse")] // 单引号+分号注入
    [InlineData("name`whoami`")]                       // 反引号
    [InlineData("a$(calc)")]                            // 子表达式
    [InlineData("bad|pipe")]
    public void Name_Injection_Throws(string v)
    {
        Assert.Throws<ArgumentException>(() => PsSafe.Name(v));
    }

    [Fact]
    public void Name_Empty_NotAllowed_Throws()
    {
        Assert.Throws<ArgumentException>(() => PsSafe.Name(""));
    }

    [Fact]
    public void Name_Empty_AllowedReturnsEmpty()
    {
        Assert.Equal("", PsSafe.Name("", allowEmpty: true));
    }

    // ---------- Int ----------
    [Fact]
    public void Int_InRange_Ok() => Assert.Equal(8, PsSafe.Int(8, 1, 365));

    [Theory]
    [InlineData(0, 1, 365)]
    [InlineData(400, 1, 365)]
    public void Int_OutOfRange_Throws(int v, int min, int max)
    {
        Assert.Throws<ArgumentException>(() => PsSafe.Int(v, min, max));
    }

    // ---------- Quote ----------
    [Fact]
    public void Quote_EscapesSingleQuotes()
    {
        Assert.Equal("'it''s'", PsSafe.Quote("it's"));
    }
}
