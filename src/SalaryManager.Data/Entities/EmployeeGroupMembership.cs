namespace SalaryManager.Data.Entities;

public class EmployeeGroupMembership
{
    public int EmployeeGroupId { get; set; }
    public EmployeeGroup EmployeeGroup { get; set; } = null!;

    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
}
