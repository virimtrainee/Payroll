using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SalaryManager.Data.Entities;

namespace SalaryManager.App.ViewModels;

public partial class AddEmployeeViewModel : ObservableObject
{
    [ObservableProperty] private string    name          = string.Empty;
    [ObservableProperty] private decimal   baseSalary;
    [ObservableProperty] private string    accountNumber = string.Empty;
    [ObservableProperty] private string    ifscCode      = string.Empty;
    [ObservableProperty] private DateTime? joiningDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCash))]
    [NotifyPropertyChangedFor(nameof(IsIcici))]
    [NotifyPropertyChangedFor(nameof(IsOther))]
    [NotifyPropertyChangedFor(nameof(ShowBankFields))]
    private PaymentMode paymentMode = PaymentMode.OtherBank;

    public bool IsCash
    {
        get => PaymentMode == PaymentMode.Cash;
        set { if (value) PaymentMode = PaymentMode.Cash; }
    }

    public bool IsIcici
    {
        get => PaymentMode == PaymentMode.IciciBank;
        set { if (value) PaymentMode = PaymentMode.IciciBank; }
    }

    public bool IsOther
    {
        get => PaymentMode == PaymentMode.OtherBank;
        set { if (value) PaymentMode = PaymentMode.OtherBank; }
    }

    public bool ShowBankFields => PaymentMode != PaymentMode.Cash;
}
