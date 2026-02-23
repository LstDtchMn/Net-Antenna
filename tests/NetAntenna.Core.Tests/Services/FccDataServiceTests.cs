using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NetAntenna.Core.Data;
using NetAntenna.Core.Models;
using NetAntenna.Core.Services;

namespace NetAntenna.Core.Tests.Services;

public class FccDataServiceTests
{
    [Fact]
    public async Task ParseEngineeringData_ComputesWgs84CoordinatesCorrectly()
    {
        var mockDb = new Mock<IDatabaseService>();
        List<FccTower> savedTowers = null!;
        mockDb.Setup(db => db.ReplaceFccTowersAsync(It.IsAny<IEnumerable<FccTower>>()))
            .Callback<IEnumerable<FccTower>>(towers => savedTowers = towers.ToList());

        var mockLogger = new Mock<ILogger<FccDataService>>();

        var handler = new MockHttpMessageHandler(CreateMockTvqData());
        var httpClient = new HttpClient(handler);
        
        var service = new FccDataService(httpClient, mockDb.Object, mockLogger.Object);
        
        await service.DownloadAndIndexLmsDataAsync();

        // Assert
        savedTowers.Should().NotBeNull();
        savedTowers.Should().HaveCount(1);
        
        var tower = savedTowers.First();
        tower.FacilityId.Should().Be(12345);
        tower.CallSign.Should().Be("WXYZ-TV");
        tower.TransmitChannel.Should().Be(24);
        tower.ErpKw.Should().Be(15.5);
        tower.HaatMeters.Should().Be(345.6);
        
        // 40° 45' 15" S -> -40.754166
        tower.Latitude.Should().BeApproximately(-40.754166, 0.0001);
        
        // 73° 58' 30.5" W -> -73.975138
        tower.Longitude.Should().BeApproximately(-73.975138, 0.0001);
    }

    private static byte[] CreateMockTvqData()
    {
        var sb = new StringBuilder();
        var fields = new string[35];
        for (int i = 0; i < fields.Length; i++) fields[i] = "";
        
        fields[1] = "WXYZ-TV";
        fields[3] = "DTV";
        fields[4] = "24";
        fields[9] = "LIC";
        fields[10] = "NEW YORK";
        fields[11] = "NY";
        fields[13] = "12345";
        fields[14] = "15.5  kW";
        fields[19] = "S";
        fields[20] = "40";
        fields[21] = "45";
        fields[22] = "15";
        fields[23] = "W";
        fields[24] = "73";
        fields[25] = "58";
        fields[26] = "30.5";
        fields[31] = "345.6 m";

        sb.AppendLine(string.Join("|", fields));
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly byte[] _content;

    public MockHttpMessageHandler(byte[] content)
    {
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(_content)
        });
    }
}
