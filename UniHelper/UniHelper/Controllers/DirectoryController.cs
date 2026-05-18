using Microsoft.AspNetCore.Mvc;
using UniHelper.DTOs;

namespace UniHelper.Controllers;

[ApiController]
[Route("api/directory")]
public class DirectoryController : ControllerBase
{
    private static readonly List<DirectionDTO> Directions = new()
    {
        new DirectionDTO { Name = "Фундаментальная информатика и информационные технологии", Uni = "УрФУ", City = "Екатеринбург", Score = 273, Budget = 120, Contract = 40, Profile = "IT" },
        new DirectionDTO { Name = "Программная инженерия", Uni = "УрФУ", City = "Екатеринбург", Score = 264, Budget = 100, Contract = 50, Profile = "IT" },
        new DirectionDTO { Name = "Информатика и вычислительная техника (AI)", Uni = "УрФУ", City = "Екатеринбург", Score = 246, Budget = 110, Contract = 45, Profile = "IT" },
        new DirectionDTO { Name = "Прикладная математика", Uni = "УрФУ", City = "Екатеринбург", Score = 218, Budget = 90, Contract = 35, Profile = "Математика" },
        new DirectionDTO { Name = "Строительство", Uni = "УрФУ", City = "Екатеринбург", Score = 213, Budget = 150, Contract = 80, Profile = "Строительство" },
        
        new DirectionDTO { Name = "Прикладная математика и информатика", Uni = "ИТМО", City = "Санкт-Петербург", Score = 305, Budget = 80, Contract = 120, Profile = "IT" },
        new DirectionDTO { Name = "Программная инженерия", Uni = "ИТМО", City = "Санкт-Петербург", Score = 284, Budget = 90, Contract = 110, Profile = "IT" },
        new DirectionDTO { Name = "Информационные системы и технологии", Uni = "ИТМО", City = "Санкт-Петербург", Score = 282, Budget = 85, Contract = 100, Profile = "IT" },
        new DirectionDTO { Name = "Информационная безопасность", Uni = "ИТМО", City = "Санкт-Петербург", Score = 275, Budget = 60, Contract = 50, Profile = "IT" },
        new DirectionDTO { Name = "Физика", Uni = "ИТМО", City = "Санкт-Петербург", Score = 282, Budget = 70, Contract = 40, Profile = "Физика" },
        
        new DirectionDTO { Name = "Строительство (Промышленное и гражданское)", Uni = "МГСУ", City = "Москва", Score = 259, Budget = 200, Contract = 150, Profile = "Строительство" },
        new DirectionDTO { Name = "Архитектура", Uni = "МГСУ", City = "Москва", Score = 169, Budget = 50, Contract = 80, Profile = "Архитектура" },
        new DirectionDTO { Name = "Градостроительство", Uni = "МГСУ", City = "Москва", Score = 169, Budget = 60, Contract = 70, Profile = "Архитектура" },
        new DirectionDTO { Name = "Прикладная математика (Цифровое проектирование)", Uni = "МГСУ", City = "Москва", Score = 226, Budget = 70, Contract = 50, Profile = "Математика" },
        new DirectionDTO { Name = "Реконструкция и реставрация", Uni = "МГСУ", City = "Москва", Score = 162, Budget = 40, Contract = 40, Profile = "Архитектура" },
        
        new DirectionDTO { Name = "Прикладная математика и информатика", Uni = "МГТУ Баумана", City = "Москва", Score = 295, Budget = 100, Contract = 60, Profile = "IT" },
        new DirectionDTO { Name = "Прикладная математика (ИИ)", Uni = "МГТУ Баумана", City = "Москва", Score = 287, Budget = 90, Contract = 55, Profile = "Математика" },
        new DirectionDTO { Name = "Информатика и вычислительная техника", Uni = "МГТУ Баумана", City = "Москва", Score = 262, Budget = 110, Contract = 70, Profile = "IT" },
        new DirectionDTO { Name = "Механика и математическое моделирование", Uni = "МГТУ Баумана", City = "Москва", Score = 271, Budget = 80, Contract = 45, Profile = "Математика" },
        new DirectionDTO { Name = "Математика и компьютерные науки", Uni = "МГТУ Баумана", City = "Москва", Score = 277, Budget = 85, Contract = 50, Profile = "IT" },
        
        new DirectionDTO { Name = "Прикладная математика и информатика (Современное программирование)", Uni = "СПбГУ", City = "Санкт-Петербург", Score = 293, Budget = 75, Contract = 65, Profile = "IT" },
        new DirectionDTO { Name = "Математика и компьютерные науки (AI360)", Uni = "СПбГУ", City = "Санкт-Петербург", Score = 297, Budget = 70, Contract = 60, Profile = "IT" },
        new DirectionDTO { Name = "Математика", Uni = "СПбГУ", City = "Санкт-Петербург", Score = 279, Budget = 80, Contract = 50, Profile = "Математика" },
        new DirectionDTO { Name = "Фундаментальная информатика", Uni = "СПбГУ", City = "Санкт-Петербург", Score = 258, Budget = 85, Contract = 55, Profile = "IT" },
        new DirectionDTO { Name = "Прикладная математика и информатика (Процессы управления)", Uni = "СПбГУ", City = "Санкт-Петербург", Score = 228, Budget = 90, Contract = 60, Profile = "Математика" },
        
        new DirectionDTO { Name = "Разработка программно-информационных систем (очень длинное название для проверки обрезки текста)", Uni = "ИТМО", City = "Санкт-Петербург", Score = 295, Budget = 80, Contract = 150, Profile = "IT" },
        new DirectionDTO { Name = "Строительство высотных зданий", Uni = "МГСУ", City = "Москва", Score = 235, Budget = 250, Contract = 100, Profile = "Строительство" },
        new DirectionDTO { Name = "Биоинженерия", Uni = "СПбГУ", City = "Санкт-Петербург", Score = 280, Budget = 30, Contract = 20, Profile = "Биология" },
        new DirectionDTO { Name = "Информационная безопасность", Uni = "МГТУ Баумана", City = "Москва", Score = 290, Budget = 100, Contract = 50, Profile = "IT" }
    };

    [HttpGet]
    public IActionResult GetDirections()
    {
        return Ok(Directions);
    }
}
