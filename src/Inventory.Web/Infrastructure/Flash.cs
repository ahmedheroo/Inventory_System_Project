using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Inventory.Web.Infrastructure;

/// <summary>
/// The one-line outcome of an action, carried through the redirect that follows it:
/// "Received 5 units into Cairo Main". The wording is put together where the
/// operation is, so the console never reports an action it did not take.
/// </summary>
public static class Flash
{
    private const string MessageKey = "flash.message";
    private const string ToneKey = "flash.tone";

    public const string Confirmed = "ok";
    public const string Note = "attention";

    public static void Confirm(this ITempDataDictionary tempData, string message) =>
        Set(tempData, message, Confirmed);

    public static void Warn(this ITempDataDictionary tempData, string message) =>
        Set(tempData, message, Note);

    public static string? Message(ITempDataDictionary tempData) =>
        tempData[MessageKey] as string;

    public static string Tone(ITempDataDictionary tempData) =>
        tempData[ToneKey] as string ?? Confirmed;

    private static void Set(ITempDataDictionary tempData, string message, string tone)
    {
        tempData[MessageKey] = message;
        tempData[ToneKey] = tone;
    }
}
