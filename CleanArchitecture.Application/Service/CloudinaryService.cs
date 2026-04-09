using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.Model.Image;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace CleanArchitecture.Application.Service
{
    public class CloudinaryService : ICloudinaryService
    {
        private readonly Cloudinary _cloudinary;

        public CloudinaryService(IConfiguration config)
        {
            var section = config.GetSection("Cloudinary");

            var account = new Account(
                section["CloudName"],
                section["ApiKey"],
                section["ApiSecret"]
            );

            _cloudinary = new Cloudinary(account);
            _cloudinary.Api.Secure = true;
        }

        public async Task<ImageUploadResponse> UploadImageAsync(IFormFile file, string folder, CancellationToken ct = default)
        {
            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
            if (!allowedTypes.Contains(file.ContentType))
                throw new ArgumentException("Invalid file type. Only JPEG, PNG, WEBP, GIF are allowed.");

            if (file.Length > 10 * 1024 * 1024)
                throw new ArgumentException("File size must be less than 10MB.");

            await using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = folder,           // "splendor-cards"
                UseFilename = true,        // dùng tên file gốc làm publicId
                UniqueFilename = false,    // không thêm suffix random → overwrite nếu trùng tên
                Overwrite = true,
                Transformation = new Transformation()
                    .Quality("auto")
                    .FetchFormat("auto")
            };

            var result = await _cloudinary.UploadAsync(uploadParams, ct);

            if (result.Error != null)
                throw new Exception($"Cloudinary upload failed: {result.Error.Message}");

            return new ImageUploadResponse
            {
                Url = result.SecureUrl.ToString(),
                PublicId = result.PublicId  // "splendor-cards/ten-file"
            };
        }

        public async Task<List<ImageUploadResponse>> UploadMultipleImagesAsync(IList<IFormFile> files, string folder, CancellationToken ct = default)
        {
            if (files == null || files.Count == 0)
                throw new ArgumentException("No files provided.");

            if (files.Count > 50)
                throw new ArgumentException("Maximum 50 files per upload.");

            var uploadTasks = files.Select(file => UploadImageAsync(file, folder, ct));
            var results = await Task.WhenAll(uploadTasks);

            return results.ToList();
        }

        public async Task<List<ImageUploadResponse>> GetImagesByFolderAsync(string folder, CancellationToken ct = default)
        {
            var result = await _cloudinary.Search()
                .Expression($"folder:{folder}")
                .SortBy("filename", "asc")
                .MaxResults(500)
                .ExecuteAsync(ct);

            if (result.Error != null)
                throw new Exception($"Cloudinary search failed: {result.Error.Message}");

            return result.Resources.Select(r => new ImageUploadResponse
            {
                Url = r.SecureUrl,
                PublicId = r.PublicId
            }).ToList();
        }
        public async Task<bool> DeleteImageAsync(string publicId, CancellationToken ct = default)
        {
            var deleteParams = new DeletionParams(publicId);
            var result = await _cloudinary.DestroyAsync(deleteParams);
            return result.Result == "ok";
        }
    }
}
