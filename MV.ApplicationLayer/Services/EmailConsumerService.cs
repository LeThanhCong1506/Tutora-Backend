//using Confluent.Kafka;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using Microsoft.Extensions.Logging;
//using MV.ApplicationLayer.ServiceInterfaces;
//using MV.DomainLayer.DTO.RequestModel;
//using System.Text.Json;

//namespace MV.ApplicationLayer.Services
//{
//    public class EmailConsumerService : BackgroundService
//    {
//        private readonly IServiceProvider _serviceProvider;
//        private readonly ILogger<EmailConsumerService> _logger;
//        private readonly ConsumerConfig _consumerConfig;
//        private readonly string _topicName = "email_sending_topic";

//        public EmailConsumerService(IConfiguration configuration, IServiceProvider serviceProvider, ILogger<EmailConsumerService> logger)
//        {
//            _serviceProvider = serviceProvider;
//            _logger = logger;

//            _consumerConfig = new ConsumerConfig
//            {
//                BootstrapServers = configuration["Kafka:BootstrapServers"],
//                GroupId = "email_consumer_group",
//                AutoOffsetReset = AutoOffsetReset.Earliest,
//            };
//        }

//        // --- THAY ĐỔI LỚN Ở ĐÂY ---
//        // Phương thức này chỉ khởi động tác vụ nền và kết thúc ngay lập tức, không còn block nữa.
//        protected override Task ExecuteAsync(CancellationToken stoppingToken)
//        {
//            _logger.LogInformation("EmailConsumerService is starting.");

//            // Chạy vòng lặp consumer trên một thread riêng biệt để không chặn việc khởi động ứng dụng
//            Task.Run(() => StartConsumerLoop(stoppingToken), stoppingToken);

//            return Task.CompletedTask;
//        }

//        // Toàn bộ logic xử lý Kafka được chuyển vào phương thức riêng này
//        private async Task StartConsumerLoop(CancellationToken stoppingToken)
//        {
//            using (var consumer = new ConsumerBuilder<Ignore, string>(_consumerConfig).Build())
//            {
//                consumer.Subscribe(_topicName);
//                _logger.LogInformation("Successfully subscribed to topic '{Topic}'. Ready to consume messages.", _topicName);

//                while (!stoppingToken.IsCancellationRequested)
//                {
//                    try
//                    {
//                        var consumeResult = consumer.Consume(stoppingToken);

//                        if (consumeResult is null || consumeResult.IsPartitionEOF) continue;

//                        _logger.LogInformation($"Received message: {consumeResult.Message.Value}");

//                        using (var scope = _serviceProvider.CreateScope())
//                        {
//                            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
//                            var emailRequest = JsonSerializer.Deserialize<EmailRequest>(consumeResult.Message.Value);

//                            if (emailRequest != null)
//                            {
//                                await emailService.SendEmailAsync(emailRequest.To, emailRequest.Subject!, emailRequest.Body!);
//                                _logger.LogInformation($"Successfully sent email to {emailRequest.To}");
//                            }
//                        }
//                    }
//                    catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
//                    {
//                        _logger.LogWarning("Topic '{Topic}' not available yet. Waiting 5 seconds before retrying...", _topicName);
//                        await Task.Delay(5000, stoppingToken);
//                    }
//                    catch (OperationCanceledException)
//                    {
//                        _logger.LogInformation("EmailConsumerService is stopping.");
//                        break;
//                    }
//                    catch (Exception ex)
//                    {
//                        _logger.LogError(ex, "An unexpected error occurred while consuming Kafka message. Retrying in 5 seconds...");
//                        await Task.Delay(5000, stoppingToken);
//                    }
//                }

//                consumer.Close();
//            }
//        }
//    }
//}
