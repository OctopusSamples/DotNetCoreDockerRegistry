using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace Controllers
{
    [Route("v2/")]
    public class DockerRegistry : ControllerBase
    {
        const string LayerPath = "C:\\Temp\\layers";

        [HttpGet("{*path}")]
        [AllowAnonymous]
        public IActionResult GetFallback(string path)
        {
            return NotFound();
        }

        [HttpPut("{*path}")]
        [AllowAnonymous]
        public IActionResult PutFallback(string path)
        {
            return NotFound();
        }

        [HttpPost("{*path}")]
        [AllowAnonymous]
        public IActionResult PostFallback(string path)
        {
            return NotFound();
        }

        [HttpPatch("{*path}")]
        [AllowAnonymous]
        public IActionResult PatchFallback(string path)
        {
            return NotFound();
        }

        [HttpDelete("{*path}")]
        [AllowAnonymous]
        public IActionResult DeleteFallback(string path)
        {
            return NotFound();
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Root(string path)
        {
            return Ok();
        }

        [HttpHead("{name}/blobs/{digest}")]
        [AllowAnonymous]
        public IActionResult Exists(string name, string digest)
        {
            var hash = digest.Split(":").Last();

            if (System.IO.File.Exists(LayerPath + "\\" + hash))
            {
                Response.Headers.Add("Content-Length", new FileInfo(LayerPath + "\\" + hash).Length.ToString());
                Response.Headers.Add("Docker-Content-Digest", digest);
                return Ok();
            }

            return NotFound();
        }

        [HttpGet("{name}/blobs/{digest}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetLayer(string name, string digest)
        {
            var hash = digest.Split(":").Last();
            var path = LayerPath + "\\" + hash;

            if (System.IO.File.Exists(LayerPath + "\\" + hash))
            {
                Response.Headers.Add("content-length", new FileInfo(path).Length.ToString());
                using (var fs = new FileStream(path, FileMode.Open))
                {
                    await fs.CopyToAsync(Response.Body);
                    return Ok();
                }
            }

            return NotFound();
        }

        [HttpPost("{name}/blobs/uploads")]
        [AllowAnonymous]
        public IActionResult StartUpload(string name)
        {
            var digest = Request.Query["digest"].FirstOrDefault();
            var guid = Guid.NewGuid().ToString();
            Response.Headers.Add("location", "/v2/" + name + "/blobs/uploads/" + guid);
            Response.Headers.Add("range", "0-0");
            Response.Headers.Add("content-length", "0");
            Response.Headers.Add("docker-upload-uuid", guid);
            return Accepted();
        }

        [HttpPatch("{name}/blobs/uploads/{uuid}")]
        [AllowAnonymous]
        public async Task<IActionResult> Upload(string name, string uuid)
        {
            var digest = Request.Query["digest"].FirstOrDefault();
            var start = Request.Headers["content-range"].FirstOrDefault()?.Split("-")[0] ?? "0";
            using FileStream fs = System.IO.File.OpenWrite(LayerPath + "\\" + uuid);
            fs.Seek(long.Parse(start), SeekOrigin.Begin);
            await Request.Body.CopyToAsync(fs);

            Response.Headers["range"] = "0-" + (fs.Position - 1);
            Response.Headers["docker-upload-uuid"] = uuid;
            Response.Headers["location"] = $"/v2/{name}/blobs/uploads/{uuid}";
            Response.Headers["content-length"] = "0";
            Response.Headers["docker-distribution-api-version"] = "registry/2.0";
            return Accepted();
        }

        [HttpPut("{name}/blobs/uploads/{uuid}")]
        [AllowAnonymous]
        public async Task<IActionResult> FinishUpload(string name, string uuid)
        {
            if (Request.Headers["Content-Length"].First() != "0")
            {
                var ranges = Request.Headers["Content-Range"].First().Split("-");
                using FileStream fs = System.IO.File.OpenWrite(LayerPath + "\\" + uuid);
                fs.Seek(long.Parse(ranges[0]), SeekOrigin.Begin);
                await Request.Body.CopyToAsync(fs);
            }

            var rawDigest = Request.Query["digest"];
            var digest = Request.Query["digest"].First().Split(":").Last();
            System.IO.File.Move(LayerPath + "\\" + uuid, LayerPath + "\\" + digest);
            Response.Headers.Add("Content-Length", "0");
            Response.Headers.Add("Docker-Content-Digest", rawDigest);

            return Created("/v2/" + name + "/blobs/" + digest, "");
        }

        [HttpHead("/v2/{name}/manifests/{reference}")]
        [AllowAnonymous]
        public IActionResult ManifestExists(string name, string reference)
        {
            var path = LayerPath + "\\" + name + "." + reference + ".json";

            if (System.IO.File.Exists(path))
            {
                Response.Headers.Add("docker-content-digest", "sha256:" + Sha256Hash(path));
                Response.Headers.Add("content-length", new FileInfo(path).Length.ToString());

                var content = System.IO.File.ReadAllText(path);
                var mediaType = JObject.Parse(content)["mediaType"].ToString();

                Response.Headers.Add("content-type", mediaType);

                return Ok();
            }

            return NotFound();
        }

        [HttpGet("/v2/{name}/manifests/{reference}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetManifest(string name, string reference)
        {
            var hash = reference.Split(":").Last();
            var path = LayerPath + "\\" + name + "." + reference + ".json";
            var hashPath = LayerPath + "\\" + hash + ".json";
            var testedPath = System.IO.File.Exists(path) ? path :
                System.IO.File.Exists(hashPath) ? hashPath :
                null;

            if (testedPath != null)
            {
                Response.Headers.Add("docker-content-digest", "sha256:" + Sha256Hash(testedPath));

                var content = System.IO.File.ReadAllText(testedPath);
                var mediaType = JObject.Parse(content)["mediaType"].ToString();

                Response.Headers.Add("content-type", mediaType);
                Response.Headers.Add("content-length", new FileInfo(testedPath).Length.ToString());

                using (var fs = new FileStream(testedPath, FileMode.Open))
                {
                    await fs.CopyToAsync(Response.Body);
                }

                return Ok();
            }

            return NotFound();
        }

        [HttpPut("/v2/{name}/manifests/{reference}")]
        [AllowAnonymous]
        public async Task<IActionResult> SaveManifest(string name, string reference)
        {
            var path = LayerPath + "\\" + name + "." + reference + ".json";

            var hash = Sha256Hash(path);
            Response.Headers.Add("docker-content-digest", "sha256:" + hash);

            using (FileStream fs = System.IO.File.OpenWrite(path))
            {
                await Request.Body.CopyToAsync(fs);
            }

            System.IO.File.Copy(path, LayerPath + "\\" + hash + ".json", true);

            return Created($"/v2/{name}/manifests/{reference}", null);
        }

        public static String Sha256Hash(string path)
        {
            StringBuilder sb = new StringBuilder();

            using (FileStream fileStream = System.IO.File.OpenRead(path))
            {
                using (var hash = SHA256.Create())
                {
                    Encoding enc = Encoding.UTF8;
                    Byte[] result = hash.ComputeHash(fileStream);

                    foreach (Byte b in result)
                        sb.Append(b.ToString("x2"));
                }
            }

            return sb.ToString();
        }
    }
}