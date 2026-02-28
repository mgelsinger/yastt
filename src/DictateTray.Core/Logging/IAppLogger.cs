namespace DictateTray.Core.Logging;

public interface IAppLogger
{
    void Info(string message);

    void Warn(string message);

    void Error(string message);

    void Error(Exception ex, string message);
}
