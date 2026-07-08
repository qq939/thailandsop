namespace VideoInferenceDemo.ImageInspection.Roi;

public sealed class InspectionRoiContextViewModel
{
    public InspectionRoiContextViewModel(string taskId, string productModel, string positionNo)
    {
        TaskId = string.IsNullOrWhiteSpace(taskId) ? "task-main" : taskId.Trim();
        ProductModel = string.IsNullOrWhiteSpace(productModel) ? "A100" : productModel.Trim();
        PositionNo = string.IsNullOrWhiteSpace(positionNo) ? "P01" : positionNo.Trim();
    }

    public string TaskId { get; }

    public string ProductModel { get; }

    public string PositionNo { get; }

    public string DisplayName => $"{TaskId} / {ProductModel} / {PositionNo}";

    public bool Matches(string taskId, string productModel, string positionNo)
    {
        return string.Equals(TaskId, taskId?.Trim(), System.StringComparison.OrdinalIgnoreCase) &&
               string.Equals(ProductModel, productModel?.Trim(), System.StringComparison.OrdinalIgnoreCase) &&
               string.Equals(PositionNo, positionNo?.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }
}
