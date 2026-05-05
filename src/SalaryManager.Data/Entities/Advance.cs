using System;
using System.ComponentModel.DataAnnotations;

namespace SalaryManager.Data.Entities;

public enum AdvanceEntryType
{
    Given = 0,
    Deducted = 1
}

public class Advance
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public AdvanceEntryType EntryType { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}
