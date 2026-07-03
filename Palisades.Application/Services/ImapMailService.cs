using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using Palisades.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Palisades.Services
{
    /// <summary>
    /// Service IMAP pour compter les mails non lus et récupérer les sujets (Zimbra, etc.).
    /// </summary>
    public class ImapMailService : IImapMailService, IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;
        private ImapClient? _client;
        private readonly object _clientLock = new object();

        public ImapMailService(string? host, int port, string? username, string? password)
        {
            _host = host ?? "";
            _port = port > 0 ? port : 993;
            _username = username ?? "";
            _password = password ?? "";
        }

        public bool IsConnected
        {
            get { lock (_clientLock) { return _client?.IsConnected == true; } }
        }

        public async Task ConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(_host))
                throw new InvalidOperationException("IMAP host is required.");
            ImapClient localClient;
            lock (_clientLock)
            {
                if (_client?.IsConnected == true)
                    return;
                _client?.Dispose();
                _client = new ImapClient();
                localClient = _client;
            }
            await localClient.ConnectAsync(_host, _port, SecureSocketOptions.SslOnConnect).ConfigureAwait(false);
            await localClient.AuthenticateAsync(_username, _password).ConfigureAwait(false);
        }

        public void Disconnect()
        {
            lock (_clientLock)
            {
                if (_client == null) return;
                if (_client.IsConnected)
                    _client.Disconnect(false);
                _client.Dispose();
                _client = null;
            }
        }

        public void Dispose()
        {
            Disconnect();
            GC.SuppressFinalize(this);
        }

        /// <summary>Retourne le nombre de messages non lus dans le dossier (IMAP STATUS).</summary>
        public async Task<int> GetUnreadCountAsync(string folderName)
        {
            EnsureConnected();
            var folder = await _client!.GetFolderAsync(folderName).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadOnly).ConfigureAwait(false);
            var count = folder.Unread;
            await folder.CloseAsync().ConfigureAwait(false);
            return count;
        }

        /// <summary>Liste des noms de dossiers (récursif, FullName).</summary>
        public async Task<List<string>> GetFolderNamesAsync()
        {
            EnsureConnected();
            var result = new List<string>();
            await CollectFoldersAsync(_client!.GetFolder(_client.PersonalNamespaces[0]), result).ConfigureAwait(false);
            return result;
        }

        private static async Task CollectFoldersAsync(IMailFolder parent, List<string> result)
        {
            var subfolders = await parent.GetSubfoldersAsync(false).ConfigureAwait(false);
            foreach (var folder in subfolders)
            {
                result.Add(folder.FullName);
                await CollectFoldersAsync(folder, result).ConfigureAwait(false);
            }
        }

        /// <summary>Récupère les N derniers mails non lus (enveloppe : expéditeur, sujet, date).</summary>
        public async Task<List<MailSummaryItem>> GetRecentUnreadSubjectsAsync(string folderName, int maxCount)
        {
            EnsureConnected();

            // Résolution par nom complet (GetFolderAsync), comme GetUnreadCountAsync : gère aussi bien
            // les dossiers racine (Junk, Archive…) que les sous-dossiers, contrairement à
            // Inbox.GetSubfolderAsync qui supposait à tort un sous-dossier d'INBOX.
            IMailFolder folder = string.Equals(folderName, "INBOX", StringComparison.OrdinalIgnoreCase)
                ? _client!.Inbox
                : await _client!.GetFolderAsync(folderName).ConfigureAwait(false);

            await folder.OpenAsync(FolderAccess.ReadOnly).ConfigureAwait(false);
            try
            {
                var uids = await folder.SearchAsync(SearchQuery.NotSeen).ConfigureAwait(false);
                if (uids.Count == 0)
                    return new List<MailSummaryItem>();
                var take = Math.Min(maxCount, uids.Count);
                var lastUids = uids.TakeLast(take).ToArray();
                var summaries = await folder.FetchAsync(lastUids, MessageSummaryItems.Envelope | MessageSummaryItems.InternalDate).ConfigureAwait(false);
                var result = new List<MailSummaryItem>();
                foreach (var s in summaries.OrderByDescending(x => x.InternalDate))
                {
                    var env = s.Envelope;
                    var from = env?.From?.ToString() ?? "";
                    if (from.Length > 50) from = string.Concat(from.AsSpan(0, 47), "...");
                    result.Add(new MailSummaryItem
                    {
                        Sender = from,
                        Subject = env?.Subject ?? "",
                        Date = s.InternalDate?.DateTime ?? default
                    });
                }
                return result;
            }
            finally
            {
                // Ferme le dossier ouvert (GetUnreadCountAsync le faisait, pas cette méthode).
                if (folder.IsOpen)
                    await folder.CloseAsync().ConfigureAwait(false);
            }
        }

        private void EnsureConnected()
        {
            lock (_clientLock)
            {
                if (_client == null || !_client.IsConnected)
                    throw new InvalidOperationException("IMAP client is not connected. Call ConnectAsync first.");
            }
        }
    }
}
