using System.IO.Compression;
using Amazon;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace svtp_web_portal_pack_template_items;

public class Function
{
    private const string BucketName = "svtp-webportal-dev";

    /// <summary>
    /// A simple function that takes a string and does a ToUpper
    /// </summary>
    /// <param name="input"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task<string> FunctionHandler(Parameter parameter, ILambdaContext context)
    {
            using (var s3Client = new AmazonS3Client(RegionEndpoint.USWest2))
            {
                var result = await s3Client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = BucketName,
                    Prefix = $"Templates/{parameter.TemplateVersion}/",
                }, CancellationToken.None);
                
                var tasks = new List<Task<GetObjectResponse>>();
                foreach (var s3Object in result.S3Objects)
                {
                    tasks.Add(s3Client.GetObjectAsync(BucketName, s3Object.Key, CancellationToken.None));
                }

                var files = await Task.WhenAll(tasks.ToArray());
                await using var zipFileStream = new MemoryStream();
                using var zipArchive = new ZipArchive(zipFileStream, ZipArchiveMode.Create);
                foreach (var file in files)
                {
                    await AddToZip(zipArchive, file.Key.Replace("/", "_"), file.ResponseStream);
                }
                zipArchive.Dispose();
                
                var uploadZipFileKey = $"Download/{parameter.TemplateVersion}_{parameter.Project}_{parameter.Phase}_{DateTime.Now.ToString("yyyyMMddHmsffff")}.zip";
                var uploadResult = await s3Client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = BucketName,
                    Key = uploadZipFileKey,
                    InputStream = new MemoryStream(zipFileStream.ToArray())
                });
                var preSignedUrl = s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
                {
                    BucketName = BucketName,
                    Key = uploadZipFileKey,
                    Verb = HttpVerb.GET,
                    Expires = DateTime.UtcNow.AddMinutes(30)
                });
                return preSignedUrl;
            }
    }
    private async Task AddToZip(ZipArchive zipArchive, string fileName, Stream fileStream)
    {
        var zipArchiveEntry = zipArchive.CreateEntry(fileName, CompressionLevel.Optimal);
        await using var zipEntryStream = zipArchiveEntry.Open();
        await fileStream.CopyToAsync(zipEntryStream);
    }
}
