namespace OBBX.Shared.Services;

public interface IFormFactor
{
    /// <summary>
    /// Returns a string representing the form factor of the device (e.g., "Desktop", "Mobile", "Tablet").
    /// </summary>
    /// <returns></returns>
    public string GetFormFactor();

    /// <summary>
    /// Returns a string representing the platform of the device (e.g., "Windows", "iOS", "Android").
    /// </summary>
    /// <returns></returns>
    public string GetPlatform();
}
