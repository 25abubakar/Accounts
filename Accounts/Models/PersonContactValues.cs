namespace Accounts.Models;

/// <summary>Display values from loaded normalized contacts; SQL uses the same ordering.</summary>
public static class PersonContactValues
{
    public static string? GetPrimaryContact(this Person? person, string type) => person?.Contacts
        .Where(c => c.ContactType == type && !string.IsNullOrWhiteSpace(c.ContactValue))
        .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Id)
        .Select(c => c.ContactValue).FirstOrDefault();
}
