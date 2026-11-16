namespace UCE.Application.Documents.Models;
public sealed class StructureBuilderOptions
{
	public int DefaultHeadingLevel { get; set; } = 1;
	public int MaximumHeadingLevel { get; set; } = 6;
	public bool UseStyleInformation { get; set; } = true;
	public bool UseTextPatterns { get; set; } = true; 
	public bool CreateRootSection { get; set; } = true;
	public string RootSectionTitle { get; set; } = "متن اصلی سند";

}