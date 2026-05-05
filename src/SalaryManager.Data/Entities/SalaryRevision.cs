using System;
using System.ComponentModel.DataAnnotations;

namespace SalaryManager.Data.Entities;

public class SalaryRevision
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public decimal OldSalary { get; set; }
    public decimal NewSalary { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(200)]
    public string? Note { get; set; }

    public Employee Employee { get; set; } = null!;
}
