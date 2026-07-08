using System.Collections.Generic;
using System.Threading.Tasks;
using Naultinus.Model;

namespace Naultinus.Services
{
    public interface IImapMailService
    {
        bool IsConnected { get; }
        Task ConnectAsync();
        void Disconnect();
        Task<int> GetUnreadCountAsync(string folderName);
        Task<List<string>> GetFolderNamesAsync();
        Task<List<MailSummaryItem>> GetRecentUnreadSubjectsAsync(string folderName, int maxCount);
    }
}
