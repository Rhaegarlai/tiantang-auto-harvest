﻿using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using tiantang_auto_harvest.Constants;
using tiantang_auto_harvest.Exceptions;
using tiantang_auto_harvest.Models;
using tiantang_auto_harvest.Service;

namespace tiantang_auto_harvest.Jobs
{
    [DisallowConcurrentExecution]
    public class ApplyBonusCardsJob : IJob
    {
        private readonly ILogger<HarvestJob> logger;
        private readonly IServiceProvider serviceProvider;
        private readonly TiantangRemoteCallService tiantangRemoteCallService;

        private string _accessToken = "";

        public ApplyBonusCardsJob(ILogger<HarvestJob> logger, IServiceProvider serviceProvider, TiantangRemoteCallService tiantangRemoteCallService)
        {
            this.logger = logger;
            this.serviceProvider = serviceProvider;
            this.tiantangRemoteCallService = tiantangRemoteCallService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            using var scope = serviceProvider.CreateScope();
            var defaultDbContext = scope.ServiceProvider.GetService<DefaultDbContext>();
            var tiantangLoginInfo = defaultDbContext!.TiantangLoginInfo.SingleOrDefault();
            if (tiantangLoginInfo == null)
            {
                logger.LogInformation("未登录甜糖账号，跳过检查和使用加成卡");
                return;
            }

            _accessToken = tiantangLoginInfo.AccessToken;

            JsonDocument activatedBonusCardResponse;
            try
            {
                var cancellationToken = CancellationTokenHelper.GetCancellationToken();
                activatedBonusCardResponse = await tiantangRemoteCallService.RetrieveActivatedBonusCards(_accessToken, cancellationToken);
            }
            catch (ExternalApiCallException)
            {
                logger.LogError("获取当前启用的加成卡失败，请参考日志");
                return;
            }

            JsonDocument allBonusCardsResponse;
            try
            {
                var cancellationToken = CancellationTokenHelper.GetCancellationToken();
                allBonusCardsResponse = await tiantangRemoteCallService.RetrieveAllBonusCards(_accessToken, cancellationToken);
            }
            catch (ExternalApiCallException)
            {
                logger.LogError("获取全部加成卡失败，请参考日志");
                return;
            }

            await CheckAndApplyElectricBillBonus(activatedBonusCardResponse, allBonusCardsResponse);
        }

        private async Task CheckAndApplyElectricBillBonus(JsonDocument activatedBonusCardResponse, JsonDocument allBonusCardsResponse)
        {
            #region 检查电费卡数量
            var electricBillBonusCardInfo =
                allBonusCardsResponse
                    .RootElement
                    .GetProperty("data")
                    .EnumerateArray()
                    .SingleOrDefault(element => element.GetProperty("prop_id").GetInt32().ToString() == TiantangBonusCardTypes.ElectricBillBonus);

            if (electricBillBonusCardInfo.ValueKind == JsonValueKind.Undefined)
            {
                logger.LogInformation("无可用电费卡，跳过检查和启用电费卡");
                return;
            }

            var remainingElectricBillBonusCardNumber =
                electricBillBonusCardInfo.GetProperty("count").GetInt32().ToString();
            logger.LogInformation($"剩余{remainingElectricBillBonusCardNumber}张电费卡");
            #endregion

            #region 检查当前有无正在生效的电费卡
            var currentActivatedElectricBillBonus =
                activatedBonusCardResponse
                    .RootElement
                    .GetProperty("data")
                    .EnumerateArray()
                    .SingleOrDefault(element => element.GetProperty("name").GetString() == "电费卡");

            if (currentActivatedElectricBillBonus.ValueKind != JsonValueKind.Undefined)
            {
                var expireEpoch = currentActivatedElectricBillBonus.GetProperty("ended_at").GetInt32();
                var expireDate = DateTimeOffset.FromUnixTimeSeconds(expireEpoch).ToString("yyyy-MM-dd HH:mm:ss");
                logger.LogInformation($"已有激活的电费卡，将于{expireDate}过期");
                return;
            }
            #endregion

            #region 激活电费卡
            var isElectricBillBonusCardExist = allBonusCardsResponse
                .RootElement
                .GetProperty("data")
                .EnumerateArray()
                .Any(element => element.GetProperty("prop_id").GetInt32().ToString() == TiantangBonusCardTypes.ElectricBillBonus);

            if (!isElectricBillBonusCardExist)
            {
                logger.LogInformation("无可用电费卡");
                return;
            }

            logger.LogInformation("正在激活电费卡");
            
            var cancellationToken = CancellationTokenHelper.GetCancellationToken();
            await tiantangRemoteCallService.ActiveElectricBillBonusCard(_accessToken, cancellationToken);
            #endregion
        }
    }
}
