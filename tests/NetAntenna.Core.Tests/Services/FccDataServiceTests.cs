using System.IO.Compression;
using System.Text;
using FluentAssertions;
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
        // The service parses data from a downloaded zip. Since methods are private, 
        // we'll test the public DownloadAndIndexLmsDataAsync with a mocked HttpClient 
        // returning a synthesized zip file containing our test data.
        
        var mockDb = new Mock<IDatabaseService>();
        List<FccTower> savedTowers = null!;
        mockDb.Setup(db => db.ReplaceFccTowersAsync(It.IsAny<IEnumerable<FccTower>>()))
            .Callback<IEnumerable<FccTower>>(towers => savedTowers = towers.ToList());

        var handler = new MockHttpMessageHandler(CreateMockLmsZip());
        var httpClient = new HttpClient(handler);
        
        var service = new FccDataService(httpClient, mockDb.Object);
        
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

    private static byte[] CreateMockLmsZip()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            // facility.dat
            var facEntry = archive.CreateEntry("facility.dat");
            using (var writer = new StreamWriter(facEntry.Open(), Encoding.UTF8))
            {
                writer.WriteLine("12345|some|WXYZ-TV|data");
            }

            // application.dat
            var appEntry = archive.CreateEntry("application.dat");
            using (var writer = new StreamWriter(appEntry.Open(), Encoding.UTF8))
            {
                writer.WriteLine("APP_001|12345||||||LIC|more_data"); // LIC = Licensed
            }

            // tv_app_engineering.dat
            var engEntry = archive.CreateEntry("tv_app_engineering.dat");
            using (var writer = new StreamWriter(engEntry.Open(), Encoding.UTF8))
            {
                // Fields 14-22: lat_lat, lat_min, lat_sec, lat_dir, lon_lat, lon_min, lon_sec, lon_dir, channel
                // We're simulating: 40 45 15 S, 73 58 30.5 W on channel 24
                
                // Build a 32+ item array (pipe delimited)
                var fields = new string[35];
                fields[0] = "APP_001";
                fields[14] = "40";
                fields[15] = "45";
                fields[16] = "15";
                fields[17] = "S";
                fields[18] = "73";
                fields[19] = "58";
                fields[20] = "30.5";
                fields[21] = "W";
                fields[22] = "24";     // channel
                fields[24] = "15.5";   // ERP
                fields[26] = "345.6";  // HAAT
                
                writer.WriteLine(string.Join("|", fields));
            }
        }
        return memoryStream.ToArray();
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
