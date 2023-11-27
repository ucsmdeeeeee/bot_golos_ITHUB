using System;
using System.IO;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;

class Program
{
    private static TelegramBotClient botClient;
    private static long adminUserId;
    private static string connectionString = "Server=DESKTOP-1QVT81V\\SQLGIBDD;Database=golos;User Id=sa;Password=1;";
    private static int currentVotingRoundId = 0;
    private static int GetCurrentVotingRoundId()
    {
        return currentVotingRoundId;
    }
    static async Task Main()
    {
        botClient = new TelegramBotClient("6429656035:AAHsmDIE_m1NwGucoXwSI3AZWaYKGyzPvmA");
        var me = await botClient.GetMeAsync();
        Console.WriteLine($"Bot connected. BotId: {me.Id}, BotName: {me.FirstName}");

        botClient.OnMessage += Bot_OnMessage;
        botClient.OnCallbackQuery += Bot_OnCallbackQuery;

        botClient.StartReceiving();

        Console.WriteLine("Press enter to exit");
        Console.ReadLine();

        botClient.StopReceiving();
    }
    private static Dictionary<long, List<string>> userPhotoDictionary = new Dictionary<long, List<string>>();
    private static bool votingActive = false;

    private static async void Bot_OnMessage(object sender, MessageEventArgs e)
    {
        var message = e.Message;

        if (message.Text == "/admin_photo_load" && message.Chat.Id == message.From.Id)
        {
            votingActive = true;
            adminUserId = message.From.Id;
            await botClient.SendTextMessageAsync(adminUserId, "Вы - администратор. Загрузите две фотографии для голосования.");
            usersWhoVoted.Clear();

            currentVotingRoundId++;

            userPhotoDictionary[adminUserId] = new List<string>();
            return;

        }
        else if (message.Text == "/admin_push_result" && message.Chat.Id == message.From.Id)

        {
            if (votingActive)
            {
                votingActive = false;

                int votingRoundId = GetCurrentVotingRoundId();
                (int countPhoto1, int countPhoto2) = GetVoteCounts(votingRoundId);

                int totalVotes = countPhoto1 + countPhoto2;
                string percentagePhoto1 = CalculatePercentage(totalVotes, countPhoto1);
                string percentagePhoto2 = CalculatePercentage(totalVotes, countPhoto2);
                string winningPhoto = (countPhoto1 > countPhoto2) ? "photo1" : "photo2";

                if (winningPhoto == "photo1")
                {
                    await botClient.SendTextMessageAsync(adminUserId, $"Результат голосования:\nФото 1: {percentagePhoto1} голосов\nФото 2: {percentagePhoto2} голосов\nПобедитель: Фото 1");
                    await botClient.SendPhotoAsync(adminUserId, votingPhoto1);
                }
                else if (winningPhoto == "photo2")
                {
                    await botClient.SendTextMessageAsync(adminUserId, $"Результат голосования:\nФото 1: {percentagePhoto1} голосов\nФото 2: {percentagePhoto2} голосов\nПобедитель: Фото 2");
                    await botClient.SendPhotoAsync(adminUserId, votingPhoto2);
                }
            }
        }

        else if (message.From.Id == adminUserId && message.Type == MessageType.Photo)
        {
            if (userPhotoDictionary.ContainsKey(adminUserId))
            {
                userPhotoDictionary[adminUserId].Add(message.Photo[0].FileId);

                if (userPhotoDictionary[adminUserId].Count == 2)
                {
                    SavePhotosToDatabase(userPhotoDictionary[adminUserId][0], userPhotoDictionary[adminUserId][1], currentVotingRoundId);

                    await SendVotingOptions(userPhotoDictionary[adminUserId][0], userPhotoDictionary[adminUserId][1]);

                    userPhotoDictionary.Remove(adminUserId);

                    usersWhoVoted.Clear();
                }
            }
        }
        else if (message.Text == "/start" && message.Chat.Id == message.From.Id)
        {
            SaveUserToDatabase(message.From);
        }
    }
    private static void SaveUserToDatabase(User user)
    {
        using (var connection = new SqlConnection(connectionString))
        {
            connection.Open();
            bool userExists;
            using (var checkCommand = new SqlCommand("SELECT COUNT(*) FROM Users WHERE Id = @Id", connection))
            {
                checkCommand.Parameters.AddWithValue("@Id", user.Id);
                userExists = (int)checkCommand.ExecuteScalar() > 0;
            }

            if (userExists)
            {
                using (var updateCommand = new SqlCommand("UPDATE Users SET Username = @Username, FirstName = @FirstName, LastName = @LastName, ChatId = @ChatId WHERE Id = @Id", connection))
                {
                    updateCommand.Parameters.AddWithValue("@Id", user.Id);
                    updateCommand.Parameters.AddWithValue("@Username", user.Username);
                    updateCommand.Parameters.AddWithValue("@FirstName", user.FirstName);
                    updateCommand.Parameters.AddWithValue("@LastName", user.LastName);
                    updateCommand.Parameters.AddWithValue("@ChatId", user.Id);
                    updateCommand.ExecuteNonQuery();
                }
            }
            else
            {
                using (var insertCommand = new SqlCommand("INSERT INTO Users (Id, Username, FirstName, LastName, ChatId) VALUES (@Id, @Username, @FirstName, @LastName, @ChatId)", connection))
                {
                    insertCommand.Parameters.AddWithValue("@Id", user.Id);
                    insertCommand.Parameters.AddWithValue("@Username", user.Username);
                    insertCommand.Parameters.AddWithValue("@FirstName", user.FirstName);
                    insertCommand.Parameters.AddWithValue("@LastName", user.LastName);
                    insertCommand.Parameters.AddWithValue("@ChatId", user.Id);
                    insertCommand.ExecuteNonQuery();
                }
            }
        }
    }
    private static HashSet<long> usersWhoVoted = new HashSet<long>();
    private static async void Bot_OnCallbackQuery(object sender, CallbackQueryEventArgs e)
    {
        var callbackQuery = e.CallbackQuery;
        var userId = callbackQuery.From.Id;

        try
        {
            if (!usersWhoVoted.Contains(userId))
            {
                int currentVotingRoundId = GetCurrentVotingRoundId();

                SaveVoteToDatabase(userId, callbackQuery.Data, currentVotingRoundId);

                await botClient.SendTextMessageAsync(userId, "Ваш голос учтен!");

                await botClient.EditMessageReplyMarkupAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, null);

                usersWhoVoted.Add(userId);
            }
            else if (!votingActive)
            {

                await botClient.SendTextMessageAsync(userId, "Этот раунд завершен.");
            }
            else
            {
                await botClient.SendTextMessageAsync(userId, "Вы уже проголосовали.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при обработке колбэка: {ex.Message}");
        }
    }
    private static string votingPhoto1;
    private static string votingPhoto2;
    public static async Task SendVotingOptions(string photo1, string photo2)
    {
        var uniqueId1 = Guid.NewGuid().ToString();
        var uniqueId2 = Guid.NewGuid().ToString();

        var inlineKeyboard1 = new InlineKeyboardMarkup(new[]
        {
        new[]
        {
            InlineKeyboardButton.WithCallbackData($"Голосовать за фото 1", $"vote1")
        }
    });

        var inlineKeyboard2 = new InlineKeyboardMarkup(new[]
        {
        new[]
        {
            InlineKeyboardButton.WithCallbackData($"Голосовать за фото 2", $"vote2")
        }
    });

        var message1 = await botClient.SendPhotoAsync(adminUserId, photo1, replyMarkup: inlineKeyboard1);
        var message2 = await botClient.SendPhotoAsync(adminUserId, photo2, replyMarkup: inlineKeyboard2);
        votingActive = true;

        votingPhoto1 = photo1;
        votingPhoto2 = photo2;
    }
    private static void SavePhotosToDatabase(string photo1, string photo2, int votingRoundId)
    {
        var uniqueId1 = Guid.NewGuid().ToString();
        var uniqueId2 = Guid.NewGuid().ToString();

        using (var connection = new SqlConnection(connectionString))
        {
            connection.Open();

            using (var command = new SqlCommand("INSERT INTO Photos (Photo1, Photo2, VotingRoundId) VALUES (@Photo1, @Photo2, @VotingRoundId)", connection))
            {
                command.Parameters.AddWithValue("@Photo1", uniqueId1);
                command.Parameters.AddWithValue("@Photo2", uniqueId2);
                command.Parameters.AddWithValue("@VotingRoundId", votingRoundId);
                command.ExecuteNonQuery();
            }
        }
    }
    private static void SaveVoteToDatabase(long userId, string votedPhoto, int votingRoundId)
    {
        using (var connection = new SqlConnection(connectionString))
        {
            connection.Open();

            using (var command = new SqlCommand("INSERT INTO Votes (UserId, VotedPhoto, VotingRoundId) VALUES (@UserId, @VotedPhoto, @VotingRoundId)", connection))
            {
                command.Parameters.AddWithValue("@UserId", userId);
                command.Parameters.AddWithValue("@VotedPhoto", votedPhoto);
                command.Parameters.AddWithValue("@VotingRoundId", votingRoundId);
                command.ExecuteNonQuery();
            }
        }
    }
    private static (int, int) GetVoteCounts(int votingRoundId)
    {
        using (var connection = new SqlConnection(connectionString))
        {
            connection.Open();

            using (var command = new SqlCommand("SELECT COUNT(*) FROM Votes WHERE VotingRoundId = @VotingRoundId AND VotedPhoto = @Photo", connection))
            {
                command.Parameters.AddWithValue("@VotingRoundId", votingRoundId);
                command.Parameters.AddWithValue("@Photo", "vote1");

                int countVote1 = (int)command.ExecuteScalar();

                command.Parameters["@Photo"].Value = "vote2";
                int countVote2 = (int)command.ExecuteScalar();

                return (countVote1, countVote2);
            }
        }
    }
    private static string CalculatePercentage(int totalVotes, int votes)
    {
        if (totalVotes == 0)
        {
            return "0%";
        }

        double percentage = (double)votes / totalVotes * 100;
        return $"{percentage:F2}%";
    }
}
