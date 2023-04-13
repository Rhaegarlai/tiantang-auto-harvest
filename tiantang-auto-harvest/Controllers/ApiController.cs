using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using tiantang_auto_harvest.Jobs;
using tiantang_auto_harvest.Models;
using tiantang_auto_harvest.Models.Requests;
using tiantang_auto_harvest.Models.Responses;
using tiantang_auto_harvest.Service;

namespace tiantang_auto_harvest.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class ApiController : ControllerBase
    {
        private readonly ILogger<ApiController> _logger;
        private readonly AppService _appService;
        private readonly ISchedulerFactory factory;

        public ApiController(ILogger<ApiController> logger, AppService appService, ISchedulerFactory factory) {
            _logger = logger;
            _appService = appService;
            this.factory = factory;
        }

        [HttpGet]
        public async Task<ActionResult> GetCaptchaImage()
        {
            var (captchaId, captchaUrl) = await _appService.GetCaptchaImage();
            
            _logger.LogInformation("CaptchaId是{CaptchaId}", captchaId);
            return new ObjectResult(new {captchaId, captchaUrl});
        }

        [HttpPost]
        public async Task<ActionResult> SendSms(SendSMSRequest sendSmsRequest)
        {
            await _appService.RetrieveSMSCode(
                sendSmsRequest.PhoneNumber, 
                sendSmsRequest.captchaId,
                sendSmsRequest.captchaCode);
            return new EmptyResult();
        }

        [HttpPost]
        public async Task<ActionResult> ManuallyRefreshLogin()
        {
            await _appService.RefreshLogin();
            return new EmptyResult();
        }

        [HttpPost]
        public async Task<ActionResult> VerifyCode(VerifyCodeRequest verifyCodeRequest)
        {
            await _appService.VerifySMSCode(verifyCodeRequest.PhoneNumber, verifyCodeRequest.OTPCode);
            return new EmptyResult();
        }

        [HttpGet]
        public ActionResult GetLoginInfo(bool showToken = false)
        {
            TiantangLoginInfo tiantangLoginInfo = _appService.GetCurrentLoginInfo();
            if (tiantangLoginInfo == null)
            {
                return new EmptyResult();
            }

            LoginInfoResponse response = new LoginInfoResponse();
            response.PhoneNumber = tiantangLoginInfo.PhoneNumber;
            if (showToken)
            {
                response.Token = tiantangLoginInfo.AccessToken;
            }
            else
            {
                response.Token = "MASKED";
            }

            return new JsonResult(response);
        }

        [HttpPost]
        public ActionResult UpdateNotificationChannels(SetNotificationChannelRequest setNotificationChannelRequest)
        {
            _appService.UpdateNotificationKeys(setNotificationChannelRequest);
            return new EmptyResult();
        }

        [HttpGet]
        public async Task<ActionResult> TestNotificationChannels()
        {
            await _appService.TestNotificationChannels();
            return new EmptyResult();
        }

        [HttpGet]
        public ActionResult GetNotificationKeys()
        {
            return new JsonResult(_appService.GetNotificationKeys());
        }


        [HttpGet]
        public async Task<ActionResult> ExcuteJob(string name)
        {
            IScheduler scheduler = await factory.GetScheduler();

            await scheduler.TriggerJob(new JobKey("tiantang_auto_harvest.Jobs."+name));
            return Ok();
        }
    }
}
