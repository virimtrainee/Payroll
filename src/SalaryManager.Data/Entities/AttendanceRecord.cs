namespace SalaryManager.Data.Entities;

public class AttendanceRecord
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int Year { get; set; }
    public int Month { get; set; }
    public int DaysAbsent { get; set; }
    public decimal EsicDeduction { get; set; }
    public decimal PfDeduction { get; set; }
    public decimal TdsDeduction { get; set; }
    public decimal? BaseSalaryOverride { get; set; }
    public decimal? NetSalaryOverride { get; set; }
}
