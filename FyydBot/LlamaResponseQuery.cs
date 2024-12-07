namespace FyydBot;

public class LlamaResponseQuery
{
    public string Query { get; set; }
    public string? PodcastName { get; set; }
    public string? Keywords { get; set; }
    public string? Date { get; set; }
    public LlamaResponseQueryDates? DateRange { get; set; }
}

public class LlamaResponseQueryDates
{
    public string StartDate { get; set; }

    public DateTime? StartDateValue
    {
        get
        {
            if (DateTime.TryParse(StartDate, out DateTime result)) return result;
            return null;
        }
    } 
    public string EndDate { get; set; }
    public DateTime? EndDateValue
    {
        get
        {
            if (DateTime.TryParse(EndDate, out DateTime result)) return result;
            return null;
        }
    } 
}