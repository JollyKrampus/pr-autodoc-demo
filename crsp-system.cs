using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CorrespondenceSystem.Api
{
    /// <summary>
    /// Demo API controller for a "Correspondence System" that sends documents to recipients.
    /// Single-file, in-memory implementation for clarity and testing.
    /// Endpoints:
    ///   POST   /api/correspondence/send                -> create + (simulated) send
    ///   GET    /api/correspondence/messages            -> query messages
    ///   GET    /api/correspondence/messages/{id}       -> get message detail
    ///   POST   /api/correspondence/messages/{id}/resend-> clone + resend
    ///   DELETE /api/correspondence/messages/{id}       -> cancel if queued
    ///   PUT    /api/correspondence/settings/webhook    -> set webhook URL (demo)
    ///   GET    /api/correspondence/health              -> quick health check
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class CorrespondenceController : ControllerBase
    {
        // -------------------- In-memory state (demo) --------------------
        private static readonly ConcurrentDictionary<Guid, StoredMessage> _messages = new();
        private static readonly ConcurrentDictionary<Guid, StoredAttachment> _attachments = new();
        private static volatile Uri? _webhook; // pretend delivery notifications

        private const long MaxTotalUploadBytes = 25 * 1024 * 1024; // 25 MB
        private static readonly string[] AllowedContentTypes =
        {
            MediaTypeNames.Application.Pdf,
            MediaTypeNames.Text.Plain,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" // .docx
        };

        // -------------------- Endpoints --------------------

        /// <summary>
        /// Accepts a multipart/form-data request with metadata + file attachments.
        /// Simulates sending by moving the message through Queued -> Sent.
        /// </summary>
        [HttpPost("send")]
        [RequestSizeLimit(MaxTotalUploadBytes * 2)]
        public async Task<ActionResult<SendResponse>> SendAsync([FromForm] SendRequest request, CancellationToken ct)
        {
            // Validate recipients
            if (request.Recipients is null || request.Recipients.Count == 0)
                return BadRequest("At least one recipient is required.");

            // Validate files + accumulate size
            long totalBytes = 0;
            var storedAttachmentIds = new List<Guid>();

            if (request.Attachments != null)
            {
                foreach (var file in request.Attachments)
                {
                    if (file.Length <= 0) continue;
                    totalBytes += file.Length;
                    if (totalBytes > MaxTotalUploadBytes)
                        return BadRequest($"Total attachment size exceeds {MaxTotalUploadBytes / (1024 * 1024)} MB.");

                    if (!IsAllowedContentType(file.ContentType))
                        return BadRequest($"Attachment content type not allowed: {file.ContentType}");

                    // Read bytes (demo only)
                    await using var ms = new MemoryStream();
                    await file.CopyToAsync(ms, ct);
                    var bytes = ms.ToArray();
                    var att = new StoredAttachment
                    {
                        Id = Guid.NewGuid(),
                        FileName = file.FileName,
                        ContentType = file.ContentType,
                        Length = bytes.LongLength,
                        UploadedUtc = DateTimeOffset.UtcNow,
                        Content = bytes
                    };
                    _attachments[att.Id] = att;
                    storedAttachmentIds.Add(att.Id);
                }
            }

            var id = Guid.NewGuid();
            var msg = new StoredMessage
            {
                Id = id,
                Subject = request.Subject ?? "(no subject)",
                Body = request.Body ?? string.Empty,
                CreatedUtc = DateTimeOffset.UtcNow,
                Status = MessageStatus.Queued,
                Priority = request.Priority,
                Recipients = request.Recipients.Select(r => new Recipient
                {
                    Address = r.Address,
                    DisplayName = r.DisplayName
                }).ToList(),
                AttachmentIds = storedAttachmentIds,
                Tags = request.Tags?.ToList() ?? new List<string>()
            };

            _messages[id] = msg;

            // Simulate async send pipeline (but actually perform quickly in-process)
            await Task.Delay(TimeSpan.FromMilliseconds(25), ct);
            msg.Status = MessageStatus.Sent;
            msg.SentUtc = DateTimeOffset.UtcNow;

            // Optional fake webhook notify
            if (_webhook is not null)
            {
                // No real HTTP call: just mark delivered after a tiny delay
                _ = Task.Run(async () =>
                {
                    await Task.Delay(50);
                    if (_messages.TryGetValue(id, out var inner))
                    {
                        inner.Status = MessageStatus.Delivered;
                        inner.DeliveredUtc = DateTimeOffset.UtcNow;
                    }
                });
            }

            return Ok(new SendResponse { MessageId = id, Status = msg.Status.ToString() });
        }

        /// <summary>
        /// Query messages by status and tags. Supports paging.
        /// </summary>
        [HttpGet("messages")]
        public ActionResult<PagedResult<MessageSummary>> ListAsync([FromQuery] MessageQuery query)
        {
            var items = _messages.Values.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(query.Status) &&
                Enum.TryParse<MessageStatus>(query.Status, true, out var parsed))
            {
                items = items.Where(m => m.Status == parsed);
            }

            if (query.Tag != null)
            {
                items = items.Where(m => m.Tags.Contains(query.Tag, StringComparer.OrdinalIgnoreCase));
            }

            // Sort newest first
            items = items.OrderByDescending(m => m.CreatedUtc);

            var total = items.LongCount();
            var skip = (query.Page - 1) * query.PageSize;
            var pageItems = items.Skip(skip).Take(query.PageSize)
                .Select(m => new MessageSummary
                {
                    Id = m.Id,
                    Subject = m.Subject,
                    Status = m.Status.ToString(),
                    CreatedUtc = m.CreatedUtc,
                    SentUtc = m.SentUtc,
                    DeliveredUtc = m.DeliveredUtc,
                    RecipientCount = m.Recipients.Count,
                    AttachmentCount = m.AttachmentIds.Count
                }).ToList();

            return Ok(new PagedResult<MessageSummary>
            {
                Total = total,
                Page = query.Page,
                PageSize = query.PageSize,
                Items = pageItems
            });
        }

        /// <summary>
        /// Get a single message with full details and attachment metadata.
        /// </summary>
        [HttpGet("messages/{id:guid}")]
        public ActionResult<MessageDetail> GetAsync(Guid id)
        {
            if (!_messages.TryGetValue(id, out var m))
                return NotFound();

            var atts = m.AttachmentIds
                .Where(_attachments.ContainsKey)
                .Select(aid =>
                {
                    var a = _attachments[aid];
                    return new AttachmentInfo
                    {
                        Id = a.Id,
                        FileName = a.FileName,
                        ContentType = a.ContentType,
                        Length = a.Length,
                        UploadedUtc = a.UploadedUtc
                    };
                }).ToList();

            return Ok(new MessageDetail
            {
                Id = m.Id,
                Subject = m.Subject,
                Body = m.Body,
                Status = m.Status.ToString(),
                CreatedUtc = m.CreatedUtc,
                SentUtc = m.SentUtc,
                DeliveredUtc = m.DeliveredUtc,
                Priority = m.Priority,
                Tags = m.Tags,
                Recipients = m.Recipients,
                Attachments = atts
            });
        }

        /// <summary>
        /// Resends a message by cloning it into a new one with Queued status.
        /// </summary>
        [HttpPost("messages/{id:guid}/resend")]
        public async Task<ActionResult<SendResponse>> ResendAsync(Guid id, CancellationToken ct)
        {
            if (!_messages.TryGetValue(id, out var m))
                return NotFound();

            var cloneReq = new SendRequest
            {
                Subject = $"[RESEND] {m.Subject}",
                Body = m.Body,
                Priority = m.Priority,
                Recipients = m.Recipients.Select(r => new RecipientDto { Address = r.Address, DisplayName = r.DisplayName }).ToList(),
                Tags = m.Tags.ToList()
            };

            // Rehydrate attachments from store into form-less flow (demo)
            var files = m.AttachmentIds.Where(_attachments.ContainsKey).Select(aid => _attachments[aid]).ToList();
            // Create fake form files just to reuse SendAsync logic? Simpler: bypass and create directly:
            var newId = Guid.NewGuid();
            var newMsg = new StoredMessage
            {
                Id = newId,
                Subject = cloneReq.Subject ?? m.Subject,
                Body = cloneReq.Body ?? m.Body,
                CreatedUtc = DateTimeOffset.UtcNow,
                Status = MessageStatus.Queued,
                Priority = cloneReq.Priority,
                Recipients = m.Recipients.Select(r => new Recipient { Address = r.Address, DisplayName = r.DisplayName }).ToList(),
                AttachmentIds = files.Select(f =>
                {
                    var copy = new StoredAttachment
                    {
                        Id = Guid.NewGuid(),
                        FileName = f.FileName,
                        ContentType = f.ContentType,
                        Content = f.Content.ToArray(),
                        Length = f.Length,
                        UploadedUtc = DateTimeOffset.UtcNow
                    };
                    _attachments[copy.Id] = copy;
                    return copy.Id;
                }).ToList(),
                Tags = m.Tags.ToList()
            };
            _messages[newId] = newMsg;

            await Task.Delay(25, ct);
            newMsg.Status = MessageStatus.Sent;
            newMsg.SentUtc = DateTimeOffset.UtcNow;

            return Ok(new SendResponse { MessageId = newId, Status = newMsg.Status.ToString() });
        }

        /// <summary>
        /// Cancels a queued message; cannot cancel once Sent or Delivered.
        /// </summary>
        [HttpDelete("messages/{id:guid}")]
        public ActionResult Cancel(Guid id)
        {
            if (!_messages.TryGetValue(id, out var m))
                return NotFound();

            if (m.Status != MessageStatus.Queued)
                return Conflict($"Message is {m.Status} and cannot be canceled.");

            m.Status = MessageStatus.Canceled;
            return NoContent();
        }

        /// <summary>
        /// Sets a (fake) webhook for delivery notifications.
        /// </summary>
        [HttpPut("settings/webhook")]
        public ActionResult SetWebhook([FromBody] WebhookRequest req)
        {
            if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var uri))
                return BadRequest("Invalid webhook URL.");

            _webhook = uri;
            return NoContent();
        }

        /// <summary>
        /// Quick health/status for the correspondence subsystem.
        /// </summary>
        [HttpGet("health")]
        public ActionResult<object> Health()
        {
            return Ok(new
            {
                ok = true,
                storedMessages = _messages.Count,
                storedAttachments = _attachments.Count,
                webhook = _webhook?.ToString()
            });
        }

        // -------------------- Helpers & models --------------------

        private static bool IsAllowedContentType(string? ct)
            => !string.IsNullOrWhiteSpace(ct) && AllowedContentTypes.Contains(ct, StringComparer.OrdinalIgnoreCase);

        public sealed class SendRequest
        {
            [Required, MaxLength(256)]
            public string? Subject { get; set; }

            [MaxLength(10000)]
            public string? Body { get; set; }

            /// <summary>Lower means more urgent (0..9)</summary>
            [Range(0, 9)]
            public int Priority { get; set; } = 5;

            /// <summary>List of recipients.</summary>
            [Required]
            public List<RecipientDto> Recipients { get; set; } = new();

            /// <summary>Optional application tags for searching.</summary>
            public List<string>? Tags { get; set; }

            /// <summary>Uploaded attachments (multipart/form-data).</summary>
            public IFormFileCollection? Attachments { get; set; }
        }

        public sealed class RecipientDto
        {
            [Required, EmailAddress, MaxLength(320)]
            public string Address { get; set; } = string.Empty;

            [MaxLength(128)]
            public string? DisplayName { get; set; }
        }

        public sealed class SendResponse
        {
            public Guid MessageId { get; set; }
            public string Status { get; set; } = "Queued";
        }

        public sealed class MessageQuery
        {
            public string? Status { get; set; }
            public string? Tag { get; set; }
            [Range(1, 500)] public int PageSize { get; set; } = 20;
            [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
        }

        public sealed class MessageSummary
        {
            public Guid Id { get; set; }
            public string Subject { get; set; } = "";
            public string Status { get; set; } = "";
            public DateTimeOffset CreatedUtc { get; set; }
            public DateTimeOffset? SentUtc { get; set; }
            public DateTimeOffset? DeliveredUtc { get; set; }
            public int RecipientCount { get; set; }
            public int AttachmentCount { get; set; }
        }

        public sealed class MessageDetail
        {
            public Guid Id { get; set; }
            public string Subject { get; set; } = "";
            public string Body { get; set; } = "";
            public string Status { get; set; } = "";
            public DateTimeOffset CreatedUtc { get; set; }
            public DateTimeOffset? SentUtc { get; set; }
            public DateTimeOffset? DeliveredUtc { get; set; }
            public int Priority { get; set; }
            public List<string> Tags { get; set; } = new();
            public List<Recipient> Recipients { get; set; } = new();
            public List<AttachmentInfo> Attachments { get; set; } = new();
        }

        public sealed class AttachmentInfo
        {
            public Guid Id { get; set; }
            public string FileName { get; set; } = "";
            public string ContentType { get; set; } = "";
            public long Length { get; set; }
            public DateTimeOffset UploadedUtc { get; set; }
        }

        public sealed class WebhookRequest
        {
            [Required] public string Url { get; set; } = "";
        }

        // -------------------- Internal storage records --------------------

        private sealed class StoredMessage
        {
            public Guid Id { get; set; }
            public string Subject { get; set; } = "";
            public string Body { get; set; } = "";
            public int Priority { get; set; }
            public MessageStatus Status { get; set; }
            public DateTimeOffset CreatedUtc { get; set; }
            public DateTimeOffset? SentUtc { get; set; }
            public DateTimeOffset? DeliveredUtc { get; set; }
            public List<Recipient> Recipients { get; set; } = new();
            public List<Guid> AttachmentIds { get; set; } = new();
            public List<string> Tags { get; set; } = new();
        }

        private sealed class StoredAttachment
        {
            public Guid Id { get; set; }
            public string FileName { get; set; } = "";
            public string ContentType { get; set; } = "";
            public long Length { get; set; }
            public DateTimeOffset UploadedUtc { get; set; }
            public byte[] Content { get; set; } = Array.Empty<byte>();
        }

        public sealed class Recipient
        {
            public string Address { get; set; } = "";
            public string? DisplayName { get; set; }
            public override string ToString() => string.IsNullOrWhiteSpace(DisplayName) ? Address : $"{DisplayName} <{Address}>";
        }

        public enum MessageStatus
        {
            Queued,
            Sent,
            Delivered,
            Canceled,
            Failed
        }
    }
}
