using System.Text.Json;
using Xunit;

namespace VideoInferenceDemo.Tests.Inference;

[Collection("DbSession")]
public sealed class ModelWorkspaceServiceTests
{
    [Fact]
    public void ReloadCatalog_LoadsLegacySelectionIntoPreferredModelSource()
    {
        using var context = new DesktopCoordinatorTestContext();
        context.CreateModelBundle("detector-a", "Detector A");
        context.CreateModelBundle("detector-b", "Detector B");
        var selectionPath = Path.Combine(context.RootDirectory, "model_selection.json");
        File.WriteAllText(
            selectionPath,
            JsonSerializer.Serialize(new
            {
                SelectedModelId = "detector-b"
            }));

        var service = new ModelWorkspaceService(context.RootDirectory, selectionPath);

        var snapshot = service.ReloadCatalog();

        Assert.Equal("detector-b", snapshot.PreferredModelSource?.Id);
        Assert.Null(snapshot.ActivatedModelSource);
    }

    [Fact]
    public void MarkActivatedModelSource_AlsoUpdatesPreferredModelSource()
    {
        using var context = new DesktopCoordinatorTestContext();
        context.CreateModelBundle("detector-a", "Detector A");
        context.CreateModelBundle("detector-b", "Detector B");
        var service = new ModelWorkspaceService(context.RootDirectory);
        var catalog = service.ReloadCatalog();
        var target = Assert.Single(catalog.AvailableModels.Where(model => model.Id == "detector-b"));

        var snapshot = service.MarkActivatedModelSource(target);

        Assert.Equal("detector-b", snapshot.PreferredModelSource?.Id);
        Assert.Equal("detector-b", snapshot.ActivatedModelSource?.Id);
    }

    [Fact]
    public void ReloadCatalog_ClearsActivatedModelSource_WhenCatalogEntryDisappears()
    {
        using var context = new DesktopCoordinatorTestContext();
        context.CreateModelBundle("detector-a", "Detector A");
        var service = new ModelWorkspaceService(context.RootDirectory);
        var catalog = service.ReloadCatalog();
        var model = Assert.Single(catalog.AvailableModels);
        service.MarkActivatedModelSource(model);
        Directory.Delete(model.BundleDirectory, recursive: true);

        var snapshot = service.ReloadCatalog();

        Assert.Null(snapshot.ActivatedModelSource);
    }
}
