using CleanArchitecture.Application.IService;
using Microsoft.AspNetCore.Mvc;

namespace Splendor_Game_Server.Controllers
{
    public class UploadController : ControllerBase
    {
        private readonly ICloudinaryService _cloudinaryService;

        public UploadController(ICloudinaryService cloudinaryService)
        {
            _cloudinaryService = cloudinaryService;
        }

        [HttpPost("image")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file provided." });

            try
            {
                var result = await _cloudinaryService.UploadImageAsync(file, "splendor-cards", ct);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
        [HttpPost("images")]
        [RequestSizeLimit(100 * 1024 * 1024)] // 100MB tổng
        public async Task<IActionResult> UploadMultipleImages([FromForm] IList<IFormFile> files, CancellationToken ct)
        {
            if (files == null || files.Count == 0)
                return BadRequest(new { message = "No files provided." });

            try
            {
                var results = await _cloudinaryService.UploadMultipleImagesAsync(files, "splendor-cards", ct);
                return Ok(new
                {
                    total = results.Count,
                    images = results
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
        [HttpPost("images-nobles")]
        [RequestSizeLimit(100 * 1024 * 1024)] // 100MB tổng
        public async Task<IActionResult> UploadMultipleImagesNobles([FromForm] IList<IFormFile> files, CancellationToken ct)
        {
            if (files == null || files.Count == 0)
                return BadRequest(new { message = "No files provided." });

            try
            {
                var results = await _cloudinaryService.UploadMultipleImagesAsync(files, "splendor-nobles", ct);
                return Ok(new
                {
                    total = results.Count,
                    images = results
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
        [HttpGet("images/splendor-cards")]
        public async Task<IActionResult> GetAllSplendorCards(CancellationToken ct)
        {
            try
            {
                var images = await _cloudinaryService.GetImagesByFolderAsync("splendor-cards", ct);
                return Ok(images);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
        [HttpGet("images/splendor-nobles")]
        public async Task<IActionResult> GetAllSplendorNobels(CancellationToken ct)
        {
            try
            {
                var images = await _cloudinaryService.GetImagesByFolderAsync("splendor-nobles", ct);
                return Ok(images);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpDelete("image/{publicId}")]
        public async Task<IActionResult> DeleteImage(string publicId, CancellationToken ct)
        {
            var decoded = Uri.UnescapeDataString(publicId);
            var success = await _cloudinaryService.DeleteImageAsync(decoded, ct);

            if (!success)
                return NotFound(new { message = "Image not found or already deleted." });

            return Ok(new { message = "Deleted successfully." });
        }
    }
}
