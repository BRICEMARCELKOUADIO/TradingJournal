using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TradingJournal.Application.Trades;
using TradingJournal.Application.Images;

namespace TradingJournal.Api.Controllers;

public sealed class TradeExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        var status = context.Exception switch
        {
            ValidationException => 400,
            TradeConflictException => 409,
            ImageConflictException => 409,
            ImageNotFoundException => 404,
            ImageStorageUnavailableException => 503,
            UnauthorizedAccessException => 401,
            _ => 0
        };
        if (status == 0) return;
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = status,
            Title = status == 404 ? "Ressource introuvable." : status == 401 ? "Authentification requise." : context.Exception.Message
        }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
