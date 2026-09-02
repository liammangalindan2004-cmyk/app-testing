using System;

namespace JapaneseLearning.Dashboard
{
    // Anything that can supply a ProgressReport implements this.
    // The async-style callback signature matches how the Firebase SDK
    // returns results, so swapping providers later needs zero UI changes.
    public interface IProgressDataProvider
    {
        void FetchProgressReport(Action<ProgressReport> onSuccess, Action<string> onError = null);
    }
}