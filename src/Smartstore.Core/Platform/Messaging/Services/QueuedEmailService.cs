﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Smartstore.Core;
using Smartstore.Core.Data;
using Smartstore.Core.Localization;
using Smartstore.Core.Messages;
using Smartstore.Data.Batching;
using Smartstore.Net.Mail;

namespace Smartstore.Services.Messages
{
    public partial class QueuedEmailService : AsyncDbSaveHook<QueuedEmail>, IQueuedEmailService
    {
        private readonly SmartDbContext _db;
        private readonly IMailService _mailService;
        private readonly ICommonServices _services;
        internal readonly EmailAccountSettings _emailAccountSettings;
        
        private bool? _shouldSaveToDisk = false;

        public QueuedEmailService(
            SmartDbContext db,
            IMailService mailService,
            ICommonServices services,
            EmailAccountSettings emailAccountSettings)
        {
            _db = db;
            _mailService = mailService;
            _services = services;
            _emailAccountSettings = emailAccountSettings;
        }

        // TODO: (MH) (core) Comment in, once localizer works correctly.
        //public Localizer T { get; set; } = NullLocalizer.Instance;
        public ILogger Logger { get; set; } = NullLogger.Instance;

        public virtual Task<int> DeleteAllQueuedMailsAsync()
        {
            // Do not delete e-mails which are about to be sent.
            return _db.QueuedEmails.Where(x => x.SentOnUtc.HasValue || x.SentTries >= 3).BatchDeleteAsync();
        }

        public virtual async Task<bool> SendMailsAsync(IEnumerable<QueuedEmail> queuedEmails, CancellationToken cancelToken = default)
        {
            var result = false;
            var saveToDisk = ShouldSaveToDisk();
            var groupedQueuedEmails = queuedEmails.GroupBy(x => x.EmailAccountId);

            foreach (var group in groupedQueuedEmails)
            {
                var account = group.FirstOrDefault().EmailAccount;
                
                if (cancelToken.IsCancellationRequested)
                    break;

                // Create a new connection for each account in current batch.
                await using (var client = await _mailService.ConnectAsync(account))
                {
                    // Limit email chunks to 100.
                    foreach (var batch in group.Slice(100))
                    {
                        result = await ProcessMailBatchAsync(batch, client, saveToDisk, cancelToken);
                        await _db.SaveChangesAsync();
                    }
                }
            }

            return result;
        }

        // TODO: (MH) (core) This is only used in one ocasion. Use code there (QueuedEmailController > DownloadAttachment) directly. 
        public virtual byte[] LoadQueuedEmailAttachmentBinary(QueuedEmailAttachment attachment)
        {
            Guard.NotNull(attachment, nameof(attachment));

            if (attachment.StorageLocation == EmailAttachmentStorageLocation.Blob)
            {
                return attachment.MediaStorage?.Data ?? new byte[0];
            }

            return null;
        }

        /// <summary>
        /// Sends batch of <see cref="QueuedEmail"/>.
        /// </summary>
        /// <param name="batch">Current batch of <see cref="QueuedEmail"/></param>
        /// <param name="client"><see cref="ISmtpClient"/> to use for sending mails.</param>
        /// <param name="saveToDisk">Specifies whether mails should be saved to disk.</param>
        /// <returns></returns>
        private async Task<bool> ProcessMailBatchAsync(IEnumerable<QueuedEmail> batch, ISmtpClient client, bool saveToDisk, CancellationToken cancelToken = default)
        {
            var result = false;

            foreach (var queuedEmail in batch)
            {
                if (cancelToken.IsCancellationRequested)
                    break;

                try
                {
                    var msg = ConvertEmail(queuedEmail);

                    if (saveToDisk)
                    {
                        await _mailService.SaveAsync(_emailAccountSettings.PickupDirectoryLocation, msg);
                    }
                    else
                    {
                        await client.SendAsync(msg, cancelToken);

                        if (_emailAccountSettings.MailSendingDelay > 0)
                        {
                            await Task.Delay(_emailAccountSettings.MailSendingDelay, cancelToken);
                        }
                    }

                    queuedEmail.SentOnUtc = DateTime.UtcNow;
                    result = true;
                }
                catch (Exception ex)
                {
                    // TODO: (MH) (core) Comment in, once localizer works correctly.
                    //Logger.Error(ex, string.Concat(T("Admin.Common.ErrorSendingEmail"), ": ", ex.Message));
                    result = false;
                }
                finally
                {
                    queuedEmail.SentTries += 1;
                }
            }

            return result;
        }

        /// <summary>
        /// Detects whether mails should be sent directly or be saved to disk.
        /// </summary>
        private bool ShouldSaveToDisk()
        {
            if (_shouldSaveToDisk != null)
                return (bool)_shouldSaveToDisk;

            if (_emailAccountSettings.PickupDirectoryLocation.HasValue())
            {
                if (!Directory.Exists(_emailAccountSettings.PickupDirectoryLocation))
                {
                    throw new SmartException($"The specified pickup directory does not exist. Please check '{nameof(EmailAccountSettings.PickupDirectoryLocation)}'.");
                }

                _shouldSaveToDisk = true;
            }

            _shouldSaveToDisk = false;

            return (bool)_shouldSaveToDisk;
        }

        /// <summary>
        /// Adds a semicolon seperated list of mail addresses to collection of MailAddresses.
        /// </summary>
        private static void AddEmailAddresses(string addresses, ICollection<MailAddress> target)
        {
            var arr = addresses.IsEmpty() ? null : addresses.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (arr != null && arr.Length > 0)
            {
                target.AddRange(arr.Where(x => x.Trim().HasValue()).Select(x => new MailAddress(x)));
            }
        }

        /// <summary>
        /// Converts <see cref="QueuedEmail"/> to <see cref="MailMessage"/>.
        /// </summary>
        internal static MailMessage ConvertEmail(QueuedEmail qe)
        {
            var msg = new MailMessage(
                new MailAddress(qe.To),
                qe.Subject.Replace("\r\n", string.Empty),
                qe.Body,
                new MailAddress(qe.From));

            if (qe.ReplyTo.HasValue())
            {
                msg.ReplyTo.Add(new MailAddress(qe.ReplyTo));
            }

            AddEmailAddresses(qe.CC, msg.Cc);
            AddEmailAddresses(qe.Bcc, msg.Bcc);

            if (qe.Attachments != null && qe.Attachments.Count > 0)
            {
                foreach (var qea in qe.Attachments)
                {
                    MailAttachment attachment = null;

                    if (qea.StorageLocation == EmailAttachmentStorageLocation.Blob)
                    {
                        var data = qea.MediaStorage?.Data;

                        if (data != null && data.LongLength > 0)
                        {
                            attachment = new MailAttachment(data.ToStream(), qea.Name, qea.MimeType);
                        }
                    }
                    else if (qea.StorageLocation == EmailAttachmentStorageLocation.Path)
                    {
                        var path = qea.Path;
                        if (path.HasValue())
                        {
                            // TODO: (MH) (core) Do this right.
                            //if (path[0] == '~' || path[0] == '/')
                            //{
                            //    path = CommonHelper.MapPath(VirtualPathUtility.ToAppRelative(path), false);
                            //}
                            //if (File.Exists(path))
                            //{
                            //    attachment = new MailAttachment(path, qea.MimeType) { Name = qea.Name };
                            //}
                        }
                    }
                    else if (qea.StorageLocation == EmailAttachmentStorageLocation.FileReference)
                    {
                        var file = qea.MediaFile;
                        if (file != null)
                        {
                            // TODO: (MH) (core) Uncomment when MediaService is available.
                            //var mediaFile = _services.MediaService.ConvertMediaFile(file);
                            //var stream = mediaFile.OpenRead();
                            //if (stream != null)
                            //{
                            //    attachment = new System.Net.Mail.Attachment(stream, file.Name, file.MimeType);
                            //}
                        }
                    }

                    if (attachment != null)
                    {
                        msg.Attachments.Add(attachment);
                    }
                }
            }

            return msg;
        }
    }
}
