﻿using AngleSharp.Dom;
using Smartstore.Collections;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Content.Media.Storage;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Web;
using Smartstore.Data;
using Smartstore.Http;
using Smartstore.IO;
using Smartstore.Net.Http;

namespace Smartstore.Core.DataExchange.Import
{
    public partial class MediaImporter : IMediaImporter
    {
        private readonly SmartDbContext _db;
        private readonly IWebHelper _webHelper;
        private readonly IMediaService _mediaService;
        private readonly IFolderService _folderService;
        private readonly DownloadManager _downloadManager;

        // Maps downloaded URLs to file names to not download the file again.
        // Sometimes subsequent products (e.g. associated products) share the same image.
        private readonly Dictionary<string, string> _downloadUrls = new();

        public MediaImporter(
            SmartDbContext db,
            IWebHelper webHelper,
            IMediaService mediaService,
            IFolderService folderService,
            DownloadManager downloadManager,
            DataExchangeSettings dataExchangeSettings)
        {
            _db = db;
            _webHelper = webHelper;
            _mediaService = mediaService;
            _folderService = folderService;
            _downloadManager = downloadManager;

            _downloadManager.HttpClient.Timeout = TimeSpan.FromMinutes(dataExchangeSettings.ImageDownloadTimeout);
        }

        #region Common

        public Action<ImportMessage, DownloadManagerItem> MessageHandler { get; set; }

        public virtual DownloadManagerItem CreateDownloadItem(
            IDirectory imageDirectory,
            IDirectory downloadDirectory,
            BaseEntity entity,
            string urlOrPath,
            object state = null,
            int displayOrder = 0,
            HashSet<string> fileNameLookup = null)
        {
            Guard.NotNull(imageDirectory);
            Guard.NotNull(downloadDirectory);

            if (urlOrPath.IsEmpty())
            {
                return null;
            }

            var item = new DownloadManagerItem
            {
                Entity = entity,
                Id = displayOrder,
                DisplayOrder = displayOrder,
                State = state
            };

            try
            {
                if (urlOrPath.IsWebUrl())
                {
                    // We append quality to avoid importing of image duplicates.
                    item.Url = _webHelper.ModifyQueryString(urlOrPath, "q=100");

                    if (_downloadUrls.ContainsKey(urlOrPath))
                    {
                        // URL has already been downloaded.
                        item.Success = true;
                        item.FileName = _downloadUrls[urlOrPath];
                    }
                    else
                    {
                        var fileName = WebHelper.GetFileNameFromUrl(urlOrPath) ?? Path.GetRandomFileName();
                        item.FileName = GetUniqueFileName(fileName, fileNameLookup);

                        fileNameLookup?.Add(item.FileName);
                    }

                    item.Path = GetAbsolutePath(downloadDirectory, item.FileName);
                }
                else
                {
                    item.Success = true;
                    item.FileName = PathUtility.SanitizeFileName(Path.GetFileName(urlOrPath).NullEmpty() ?? Path.GetRandomFileName());

                    item.Path = Path.IsPathRooted(urlOrPath)
                        ? urlOrPath
                        : GetAbsolutePath(imageDirectory, urlOrPath);
                }

                item.MimeType = MimeTypes.MapNameToMimeType(item.FileName);

                return item;
            }
            catch
            {
                InvokeMessageHandler($"Failed to prepare image download for '{urlOrPath}'. Skipping file.", item, ImportMessageType.Error);
                return null;
            }

            static string GetUniqueFileName(string fileName, HashSet<string> lookup)
            {
                if (lookup?.Contains(fileName) ?? false)
                {
                    var i = 0;
                    var name = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);

                    do
                    {
                        fileName = $"{name}-{++i}{ext}";
                    }
                    while (lookup.Contains(fileName));
                }

                return fileName;
            }

            static string GetAbsolutePath(IDirectory directory, string fileNameOrRelativePath)
            {
                return Path.Combine(directory.PhysicalPath, fileNameOrRelativePath.TrimStart(PathUtility.PathSeparators).Replace('/', '\\'));
            }
        }

        /// <summary>
        /// Gets a value indicating whether the download succeeded.
        /// </summary>
        /// <param name="item">Download manager item.</param>
        /// <param name="maxCachedUrls">
        /// The maximum number of internally cached download URLs.
        /// Internal caching avoids multiple downloads of identical images.
        /// </param>
        /// <returns>A value indicating whether the download succeeded.</returns>
        protected virtual bool DownloadSucceeded(DownloadManagerItem item, int maxCachedUrls = 1000)
        {
            if (item.Success && File.Exists(item.Path))
            {
                // "Cache" URL to not download it again.
                if (item.Url.HasValue() && !_downloadUrls.ContainsKey(item.Url))
                {
                    if (_downloadUrls.Count >= maxCachedUrls)
                    {
                        _downloadUrls.Clear();
                    }

                    _downloadUrls[item.Url] = Path.GetFileName(item.Path);
                }

                return true;
            }
            else
            {
                if (item.ErrorMessage.HasValue())
                {
                    InvokeMessageHandler(item.ToString(), item, ImportMessageType.Error);
                }

                return false;
            }
        }

        protected virtual void InvokeMessageHandler(
            string msg,
            DownloadManagerItem item = null,
            ImportMessageType messageType = ImportMessageType.Info,
            ImportMessageReason reason = ImportMessageReason.None)
        {
            if (MessageHandler != null && msg.HasValue())
            {
                MessageHandler.Invoke(new ImportMessage(msg, messageType, reason)
                {
                    AffectedField = $"{nameof(item.Entity)} #{item.DisplayOrder}"
                },
                item);
            }
        }

        #endregion

        #region Generic importers

        public virtual async Task<int> ImportMediaFilesManyAsync(
            DbContextScope scope,
            ICollection<DownloadManagerItem> items,
            MediaFolderNode album,
            Multimap<int, IMediaFile> existingFiles,
            Func<MediaFile, DownloadManagerItem, IMediaFile> assignMediaFileHandler,
            DuplicateFileHandling duplicateFileHandling = DuplicateFileHandling.Rename,
            CancellationToken cancelToken = default)
        {
            Guard.NotNull(scope);
            Guard.NotNull(items);
            Guard.NotNull(album);
            Guard.NotNull(existingFiles);
            Guard.NotNull(assignMediaFileHandler);

            var itemsMap = items
                .Where(x => x?.Entity != null)
                .ToMultimap(x => x.Entity.Id, x => x);

            if (itemsMap.Count == 0)
            {
                return 0;
            }

            var newFiles = new List<FileBatchSource>();
            
            foreach (var pair in itemsMap)
            {
                try
                {
                    var entityId = pair.Key;
                    var downloadItems = pair.Value;
                    var maxDisplayOrder = int.MaxValue;

                    // Be kind and assign a continuous DisplayOrder if none has been explicitly specified by the caller.
                    if (downloadItems.All(x => x.DisplayOrder == 0))
                    {
                        maxDisplayOrder = existingFiles.TryGetValues(entityId, out var pmf) && pmf.Count > 0
                            ? pmf.Select(x => x.DisplayOrder).Max()
                            : 0;
                    }

                    // Download images per product.
                    if (downloadItems.Any(x => x.Url.HasValue()))
                    {
                        // TODO: (mg) (core) Make this fire&forget somehow and sync later.
                        await _downloadManager.DownloadFilesAsync(
                            downloadItems.Where(x => x.Url.HasValue() && !x.Success),
                            cancelToken);
                    }

                    foreach (var item in downloadItems.OrderBy(x => x.DisplayOrder))
                    {
                        if (item.Entity == null)
                        {
                            InvokeMessageHandler("DownloadManagerItem does not contain the entity to which it belongs.", item, ImportMessageType.Error);
                            continue;
                        }

                        if (DownloadSucceeded(item))
                        {
                            var stream = File.OpenRead(item.Path);
                            var disposeStream = true;

                            if (stream?.Length > 0)
                            {
                                var currentFiles = existingFiles.ContainsKey(item.Entity.Id)
                                    ? existingFiles[item.Entity.Id]
                                    : Enumerable.Empty<IMediaFile>();

                                var equalityCheck = await _mediaService.FindEqualFileAsync(stream, currentFiles.Select(x => x.MediaFile), true);
                                if (equalityCheck.Success)
                                {
                                    // INFO: may occur during a initial import when products have the same SKU and
                                    // the first product was overwritten with the data of the second one.
                                    InvokeMessageHandler($"Found equal file in product data for '{item.FileName}'. Skipping file.", item, reason: ImportMessageReason.EqualFile);
                                }
                                else
                                {
                                    if (maxDisplayOrder != int.MaxValue)
                                    {
                                        item.DisplayOrder = ++maxDisplayOrder;
                                    }

                                    equalityCheck = await _mediaService.FindEqualFileAsync(stream, item.FileName, album.Id, true);
                                    if (equalityCheck.Success)
                                    {
                                        // INFO: may occur during a subsequent import when products have the same SKU and
                                        // the images of the second product are additionally assigned to the first one.
                                        var assignedFile = assignMediaFileHandler(equalityCheck.Value, item);
                                        existingFiles.Add(item.Entity.Id, assignedFile);
                                        InvokeMessageHandler($"Found equal file in {album.Name} album for '{item.FileName}'. Assigning existing file instead.", item, reason: ImportMessageReason.EqualFileInAlbum);
                                    }
                                    else
                                    {
                                        // Keep path for later batch import of new images.
                                        newFiles.Add(new FileBatchSource(MediaStorageItem.FromStream(stream, true))
                                        {
                                            FileName = item.FileName,
                                            State = item
                                        });
                                        disposeStream = false;
                                    }
                                }
                            }

                            if (disposeStream) stream?.Dispose();
                        }
                        else if (item.Url.HasValue())
                        {
                            InvokeMessageHandler($"Download failed for image {item.Url}.", item, reason: ImportMessageReason.DownloadFailed);
                        }
                    }
                }
                catch (Exception ex)
                {
                    InvokeMessageHandler(ex.ToAllMessages(), null, ImportMessageType.Warning);
                }
            }

            if (newFiles.Count > 0)
            {
                var postProcessingEnabled = _mediaService.ImagePostProcessingEnabled;

                try
                {
                    // Always turn image post-processing off during imports. It can heavily decrease processing time.
                    _mediaService.ImagePostProcessingEnabled = false;

                    var batchFileResult = await _mediaService.BatchSaveFilesAsync(
                        newFiles.ToArray(),
                        album,
                        false,
                        duplicateFileHandling,
                        cancelToken);

                    foreach (var fileResult in batchFileResult)
                    {
                        if (fileResult.Exception == null && fileResult.File?.Id > 0)
                        {
                            var item = fileResult.Source.State as DownloadManagerItem;
                            var assignedFile = assignMediaFileHandler(fileResult.File.File, item);
                            existingFiles.Add(item.Entity.Id, assignedFile);
                        }
                    }
                }
                finally
                {
                    _mediaService.ImagePostProcessingEnabled = postProcessingEnabled;
                }
            }

            await scope.CommitAsync(cancelToken);

            return newFiles.Count;
        }

        public virtual async Task<int> ImportMediaFilesAsync<T>(
            DbContextScope scope,
            ICollection<DownloadManagerItem> items,
            MediaFolderNode album,
            Action<T, int> assignMediaFileHandler,
            Func<T, Stream, Task<bool>> checkAssignedMediaFileHandler,
            bool checkExistingFile,
            DuplicateFileHandling duplicateFileHandling = DuplicateFileHandling.Rename,
            CancellationToken cancelToken = default) where T : BaseEntity
        {
            Guard.NotNull(scope);
            Guard.NotNull(items);
            Guard.NotNull(album);
            Guard.NotNull(assignMediaFileHandler);

            items = items.Where(x => x != null).ToArray();
            if (items.Count == 0)
            {
                return 0;
            }

            var newFiles = new List<FileBatchSource>();

            foreach (var item in items)
            {
                try
                {
                    var entity = item.Entity as T;
                    if (entity == null)
                    {
                        InvokeMessageHandler($"DownloadManagerItem does not contain the {nameof(T)} entity to which it belongs.", item, ImportMessageType.Error);
                        continue;
                    }

                    // Download file.
                    if (item.Url.HasValue() && !item.Success)
                    {
                        await _downloadManager.DownloadFilesAsync(new[] { item }, cancelToken);
                    }

                    if (DownloadSucceeded(item))
                    {
                        var stream = File.OpenRead(item.Path);
                        var disposeStream = true;

                        if (stream?.Length > 0)
                        {
                            // Check for already assigned files.
                            if (await checkAssignedMediaFileHandler(entity, stream))
                            {
                                InvokeMessageHandler($"Found equal file for {nameof(entity)} '{item.FileName}'. Skipping file.", item, reason: ImportMessageReason.EqualFile);
                                continue;
                            }

                            bool addFileBatchSource = true;
                            if (checkExistingFile)
                            {
                                var equalityCheck = await _mediaService.FindEqualFileAsync(stream, item.FileName, album.Id, true);
                                if (equalityCheck.Success)
                                {
                                    assignMediaFileHandler(entity, equalityCheck.Value.Id);
                                    InvokeMessageHandler($"Found equal file in {album.Name} album for '{item.FileName}'. Assigning existing file instead.", item, reason: ImportMessageReason.EqualFileInAlbum);
                                    addFileBatchSource = false;
                                }
                            }

                            if (addFileBatchSource)
                            {
                                // Keep path for later batch import of new images.
                                newFiles.Add(new FileBatchSource(MediaStorageItem.FromStream(stream, true))
                                {
                                    FileName = item.FileName,
                                    State = item.Entity
                                });
                                disposeStream = false;
                            }
                        }

                        if (disposeStream) stream?.Dispose();
                    }
                    else if (item.Url.HasValue())
                    {
                        InvokeMessageHandler($"Download failed for {nameof(entity)} {item.Url}.", item, reason: ImportMessageReason.DownloadFailed);
                    }
                }
                catch (Exception ex)
                {
                    InvokeMessageHandler(ex.ToAllMessages(), null, ImportMessageType.Warning);
                }
            }

            if (newFiles.Count > 0)
            {
                var postProcessingEnabled = _mediaService.ImagePostProcessingEnabled;

                try
                {
                    // Always turn image post-processing off during imports. It can heavily decrease processing time.
                    _mediaService.ImagePostProcessingEnabled = false;

                    var batchFileResult = await _mediaService.BatchSaveFilesAsync(
                        newFiles.ToArray(),
                        album,
                        false,
                        duplicateFileHandling,
                        cancelToken);

                    foreach (var fileResult in batchFileResult)
                    {
                        if (fileResult.Exception == null && fileResult.File?.Id > 0)
                        {
                            // Assign MediaFile to corresponding entity via callback.
                            assignMediaFileHandler((T)fileResult.Source.State, fileResult.File.Id);
                        }
                    }
                }
                finally
                {
                    _mediaService.ImagePostProcessingEnabled = postProcessingEnabled;
                }
            }

            await scope.CommitAsync(cancelToken);

            return newFiles.Count;
        }

        #endregion

        #region Specific importers

        public async Task<int> ImportProductImagesAsync(
            DbContextScope scope,
            ICollection<DownloadManagerItem> items,
            DuplicateFileHandling duplicateFileHandling = DuplicateFileHandling.Rename,
            CancellationToken cancelToken = default)
        {
            var itemIds = items
                .Where(x => x?.Entity != null)
                .Select(x => x.Entity.Id)
                .Distinct()
                .ToArray();

            var files = await _db.ProductMediaFiles
                .AsNoTracking()
                .Include(x => x.MediaFile)
                .Where(x => itemIds.Contains(x.ProductId))
                .ToListAsync(cancelToken);

            var existingFiles = files.ToMultimap(x => x.ProductId, x => x as IMediaFile);
            var album = _folderService.GetNodeByPath(SystemAlbumProvider.Catalog).Value;

            return await ImportMediaFilesManyAsync(
                scope, 
                items,
                album,
                existingFiles,
                AssignProductMediaFile,
                cancelToken: cancelToken);

            IMediaFile AssignProductMediaFile(MediaFile file, DownloadManagerItem item)
            {
                var productMediaFile = new ProductMediaFile
                {
                    ProductId = item.Entity.Id,
                    MediaFileId = file.Id,
                    DisplayOrder = item.DisplayOrder
                };

                scope.DbContext.Add(productMediaFile);

                productMediaFile.MediaFile = file;

                // Update for FixProductMainPictureIds.
                ((Product)item.Entity).UpdatedOnUtc = DateTime.UtcNow;

                return productMediaFile;
            }
        }

        public async Task<int> ImportProductImagesAsync(
            Product product,
            ICollection<FileBatchSource> items,
            DuplicateFileHandling duplicateFileHandling = DuplicateFileHandling.Rename,
            CancellationToken cancelToken = default)
        {
            Guard.NotNull(product);

            if (items.IsNullOrEmpty())
            {
                return 0;
            }

            await _db.LoadCollectionAsync(product, x => x.ProductMediaFiles, false, q => q.Include(x => x.MediaFile), cancelToken);

            var assigments = product.ProductMediaFiles;
            var newFiles = new List<FileBatchSource>();
            var newAssigments = new List<ProductMediaFile>();
            var album = _folderService.GetNodeByPath(SystemAlbumProvider.Catalog).Value;
            var displayOrder = assigments.Count > 0 ? assigments.Max(x => x.DisplayOrder) : 0;

            foreach (var item in items)
            {
                Guard.NotEmpty(item.FileName);

                if (item.State is Dictionary<string, object> customData
                    && customData.TryGetValue(nameof(ProductMediaFile.MediaFileId), out var rawFileId)
                    && rawFileId is int fileId)
                {
                    // Overwrite existing file.
                    var file = assigments.FirstOrDefault(x => x.MediaFileId == fileId);
                    if (file != null)
                    {
                        var info = _mediaService.ConvertMediaFile(file.MediaFile);
                        var updatedFile = await _mediaService.SaveFileAsync(info.Path, item.Source.SourceStream, false, DuplicateFileHandling.Overwrite);

                        if (updatedFile == null || updatedFile.Id != fileId)
                        {
                            InvokeMessageHandler($"Failed to update existing product file with ID {fileId} and path '{info.Path.NaIfEmpty()}'.", null, ImportMessageType.Error);
                        }
                        continue;
                    }
                }

                var equalityCheck = await _mediaService.FindEqualFileAsync(item.Source.SourceStream, assigments.Select(x => x.MediaFile), true);
                if (!equalityCheck.Success)
                {
                    equalityCheck = await _mediaService.FindEqualFileAsync(item.Source.SourceStream, item.FileName, album.Id, true);
                    if (equalityCheck.Success)
                    {
                        if (!assigments.Any(x => x.MediaFileId == equalityCheck.Value.Id))
                        {
                            newAssigments.Add(new ProductMediaFile
                            {
                                ProductId = product.Id,
                                MediaFileId = equalityCheck.Value.Id,
                                DisplayOrder = ++displayOrder
                            });
                        }
                    }
                    else
                    {
                        newFiles.Add(item);
                    }
                }
            }

            if (newFiles.Count > 0)
            {
                var postProcessingEnabled = _mediaService.ImagePostProcessingEnabled;

                try
                {
                    var batchFileResult = await _mediaService.BatchSaveFilesAsync(
                        newFiles.ToArray(),
                        album,
                        false,
                        duplicateFileHandling,
                        cancelToken);

                    batchFileResult
                        .Where(x => x.Exception == null && x.File?.Id > 0)
                        .Each(x => newAssigments.Add(new ProductMediaFile
                        {
                            ProductId = product.Id,
                            MediaFileId = x.File!.Id,
                            DisplayOrder = ++displayOrder
                        }));
                }
                finally
                {
                    _mediaService.ImagePostProcessingEnabled = postProcessingEnabled;
                }
            }

            if (newAssigments.Count > 0)
            {
                // INFO: FixProductMainPictureId is called by ProductMediaFileHook.
                assigments.AddRange(newAssigments);

                await _db.SaveChangesAsync(cancelToken);
            }

            return newFiles.Count;
        }

        public async Task<int> ImportCategoryImagesAsync(
            DbContextScope scope,
            ICollection<DownloadManagerItem> items,
            DuplicateFileHandling duplicateFileHandling = DuplicateFileHandling.Rename,
            CancellationToken cancelToken = default)
        {
            var itemsArr = items.Where(x => x != null).ToArray();
            if (itemsArr.Length == 0)
            {
                return 0;
            }

            var existingFileIds = itemsArr
                .Select(x => x.Entity as Category)
                .Where(x => x != null && x.MediaFileId > 0)
                .ToDistinctArray(x => x.MediaFileId.Value);

            var files = await _mediaService.GetFilesByIdsAsync(existingFileIds);
            var existingFiles = files.ToDictionary(x => x.Id, x => x.File);

            return await ImportMediaFilesAsync<Category>(
                scope,
                items,
                _folderService.GetNodeByPath(SystemAlbumProvider.Catalog).Value,
                AssignMediaFile,
                CheckAssignedFileAsync,
                true,
                cancelToken: cancelToken);

            async Task<bool> CheckAssignedFileAsync(Category category, Stream stream)
            {
                if (category.MediaFileId.HasValue && existingFiles.TryGetValue(category.MediaFileId.Value, out var assignedFile))
                {
                    var isEqualData = await _mediaService.FindEqualFileAsync(stream, new[] { assignedFile }, true);
                    if (isEqualData.Success)
                    {
                        return true;
                    }
                }

                return false;
            }

            static void AssignMediaFile(Category category, int fileId)
            {
                category.MediaFileId = fileId;
            }
        }

        public async Task<int> ImportCustomerAvatarsAsync(
            DbContextScope scope,
            ICollection<DownloadManagerItem> items,
            DuplicateFileHandling duplicateFileHandling = DuplicateFileHandling.Rename,
            CancellationToken cancelToken = default)
        {
            return await ImportMediaFilesAsync<Customer>(
                scope, 
                items,
                _folderService.GetNodeByPath(SystemAlbumProvider.Customers).Value,
                AddCustomerAvatarMediaFile,
                CheckAssignedFileAsync,
                false,
                cancelToken: cancelToken);

            async Task<bool> CheckAssignedFileAsync(Customer customer, Stream stream)
            {
                var file = await _mediaService.GetFileByIdAsync(customer.GenericAttributes.AvatarPictureId ?? 0, MediaLoadFlags.AsNoTracking);
                if (file != null)
                {
                    var isEqualData = await _mediaService.FindEqualFileAsync(stream, new[] { file.File }, true);
                    if (isEqualData.Success)
                    {
                        return true;
                    }
                }

                return false;
            }

            static void AddCustomerAvatarMediaFile(Customer customer, int fileId)
            {
                customer.GenericAttributes.Set(SystemCustomerAttributeNames.AvatarPictureId, fileId);
            }
        }

        #endregion
    }
}