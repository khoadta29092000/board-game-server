using CleanArchitecture.Domain.Model.Image;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.IService
{
    public interface ICloudinaryService
    {
        Task<ImageUploadResponse> UploadImageAsync(IFormFile file, string folder, CancellationToken ct = default);
        Task<List<ImageUploadResponse>> UploadMultipleImagesAsync(IList<IFormFile> files, string folder, CancellationToken ct = default);
        Task<List<ImageUploadResponse>> GetImagesByFolderAsync(string folder, CancellationToken ct = default);
        Task<bool> DeleteImageAsync(string publicId, CancellationToken ct = default);
    }
}
