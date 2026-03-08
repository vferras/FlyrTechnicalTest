namespace FlyrTech.Core.Models;

/// <summary>
/// Represents a travel journey with multiple segments
/// </summary>
public class Journey
{
    public string Id { get; set; } = string.Empty;
    public string PassengerName { get; set; } = string.Empty;
    public string PassengerEmail { get; set; } = string.Empty;
    public DateTime BookingDate { get; set; }
    public string Status { get; set; } = "Pending";
    public decimal TotalPrice { get; set; }
    public List<Segment> Segments { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
    public int Version { get; set; }
}

/// <summary>
/// Represents a segment within a journey
/// </summary>
public class Segment
{
    public string SegmentId { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public DateTime DepartureTime { get; set; }
    public DateTime ArrivalTime { get; set; }
    public string FlightNumber { get; set; } = string.Empty;
    public string Carrier { get; set; } = string.Empty;
    public string Status { get; set; } = "Scheduled";
    public decimal Price { get; set; }
}
