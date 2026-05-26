using CommunityToolkit.Mvvm.ComponentModel;

namespace SalaryManager.App.ViewModels;

public record GroupFilterOptionVm(int? Id, string Name)
{
    public bool IsAllGroups => Id is null;
}

public partial class SelectableGroupFilterOptionVm : ObservableObject
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;

    [ObservableProperty] private bool isSelected;
}

public partial class GroupMembershipOptionVm : ObservableObject
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;

    [ObservableProperty] private bool isSelected;
}

public partial class GroupEmployeeAssignmentVm : ObservableObject
{
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;

    [ObservableProperty] private bool isMember;
}
