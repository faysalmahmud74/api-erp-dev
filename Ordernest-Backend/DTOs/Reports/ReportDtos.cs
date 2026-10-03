namespace Ordernest.Backend.DTOs.Reports;

public record DailySalesDto(
    DateTime Date, int OrderCount, decimal GrossSales,
    decimal Discounts, decimal NetSales, decimal AverageOrderValue);

public record MonthlySalesDto(
    int Year, int Month, int OrderCount, decimal GrossSales,
    decimal Discounts, decimal NetSales);

public record TopProductDto(
    int? ProductId, string ProductName, string Sku,
    int QuantitySold, decimal Revenue);

public record SalesSummaryDto(
    DateTime Date, decimal TodaySales, int TodayOrders,
    decimal AverageOrderValue, int LowStockCount, int ActiveProducts, int TotalCustomers);

public record CashierSalesDto(
    string UserId, string CashierName, int OrderCount, decimal TotalSales);
