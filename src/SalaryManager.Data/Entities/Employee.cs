using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SalaryManager.Data.Entities;

public class Employee
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public decimal BaseSalary { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(30)]
    public string? AccountNumber { get; set; }

    [MaxLength(15)]
    public string? IfscCode { get; set; }

    public PaymentMode PaymentMode { get; set; } = PaymentMode.OtherBank;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? JoiningDate { get; set; }

    public List<AttendanceRecord> AttendanceRecords { get; set; } = new();
    public List<Advance> Advances { get; set; } = new();
    public List<SalaryRevision> SalaryRevisions { get; set; } = new();
    public List<EmployeeGroupMembership> GroupMemberships { get; set; } = new();
}
