        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Inventory.Application.Abstractions;
        using Inventory.Application.DTOs.Batches;
        using Microsoft.AspNetCore.Mvc;

        namespace Inventory.Web.Controllers.Api;

        [Route("api/products/{productId:int}/batches")]
        [ApiController]
        public sealed class ProductBatchesApiController : ControllerBase
        {
            private readonly IProductBatchServices _batches;

            public ProductBatchesApiController(IProductBatchServices batches)
            {
                _batches = batches;
            }

            [HttpGet]
            public async Task<ActionResult<IReadOnlyList<ProductBatchResponseDto>>> GetBatches(
                int productId,
                CancellationToken cancellationToken)
            {
                if (productId <= 0) return BadRequest("Invalid productId.");

                var result = await _batches.GetForProductAsync(productId, cancellationToken);
                if (result.Count == 0) return Ok(Array.Empty<ProductBatchResponseDto>());
                return Ok(result);
            }

            [HttpPatch("{batchNumber}")]
            public async Task<IActionResult> UpsertBatchMeta(
                int productId,
                string batchNumber,
                [FromBody] UpsertProductBatchRequestDto request,
                CancellationToken cancellationToken)
            {
                if (productId <= 0) return BadRequest("Invalid productId.");
                if (batchNumber is null) return BadRequest("Invalid batchNumber.");

                var updated = await _batches.UpsertAsync(productId, batchNumber, request, cancellationToken);
                return Ok(updated);
            }
        }

