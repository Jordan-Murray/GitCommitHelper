namespace GitCommitHelper.Services.Interfaces
{
    public interface ILlmService
    {
        Task<string> GetCommitMessageAsync(string diffContent);
        Task<string> GetPrDetailsAsync(string diffContent);
    }
}
