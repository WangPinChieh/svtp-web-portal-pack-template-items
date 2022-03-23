using Newtonsoft.Json;

namespace svtp_web_portal_pack_template_items;

public class Parameter
{
    [JsonProperty("zipFileRetrievalKey")]
    public string ZipFileRetrievalKey { get; set; }

    [JsonProperty("templateVersion")]
    public string TemplateVersion { get; set; }

    [JsonProperty("project")]
    public string Project { get; set; }

    [JsonProperty("phase")]
    public string Phase { get; set; }

    public override string ToString()
    {
        return $"{nameof(ZipFileRetrievalKey)}: {ZipFileRetrievalKey}, {nameof(TemplateVersion)}: {TemplateVersion}, {nameof(Project)}: {Project}, {nameof(Phase)}: {Phase}";
    }
}
