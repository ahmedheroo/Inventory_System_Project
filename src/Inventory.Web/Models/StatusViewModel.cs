namespace Inventory.Web.Models;

/// <summary>A screen the console answers with a status instead of a page: a wrong URL, a record that is gone.</summary>
public sealed record StatusViewModel(int Code, string Headline, string Explanation)
{
    public static StatusViewModel For(int code) => code switch
    {
        400 => new StatusViewModel(400, "That request could not be read", "A filter or an identifier in the address was not valid. Go back and try again."),
        404 => new StatusViewModel(404, "No such screen", "The address does not match a page in this console, or the record it names is no longer in the catalogue."),
        405 => new StatusViewModel(405, "That action is not allowed here", "The page was asked for with a method it does not accept."),
        _ => new StatusViewModel(code, "The page could not be produced", "The console answered with this status.")
    };
}
