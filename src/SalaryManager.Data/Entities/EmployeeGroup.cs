using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SalaryManager.Data.Entities;

public class EmployeeGroup
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string NormalizedName { get; set; } = string.Empty;

    public List<EmployeeGroupMembership> Memberships { get; set; } = new();
}
