namespace FootballApp.Domain.Entities;

/// <summary>Join entity — which tags (in which order) make up a given clip.</summary>
public class ClipTag
{
    public Guid ClipId { get; set; }
    public Clip? Clip { get; set; }

    public Guid TagId { get; set; }
    public Tag? Tag { get; set; }

    public int Order { get; set; }
}
