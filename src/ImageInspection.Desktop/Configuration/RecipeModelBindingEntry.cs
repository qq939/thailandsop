using System.Collections.Generic;

namespace VideoInferenceDemo.ImageInspection;

public sealed class RecipeModelBindingEntry
{
    public string ModelId { get; set; } = string.Empty;

    public string Alias { get; set; } = string.Empty;

    public int Sequence { get; set; }

    public List<string> RoiIds { get; set; } = [];
}
