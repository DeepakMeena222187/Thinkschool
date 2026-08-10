using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderRefactor
{
    [ApiController]
    [Route("api/orders")]
    public class OrderController : ControllerBase
    {
        private readonly OrderDbContext _db;

        public OrderController(OrderDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<object> Post([FromBody] object body)
        {
            await Task.Delay(5);

            var payloadText = JsonSerializer.Serialize(body);
            var responseBag = new Dictionary<string, object>();

            try
            {
                using var doc = JsonDocument.Parse(payloadText);
                var root = doc.RootElement;

                var customerName = root.TryGetProperty("customerName", out var customerNameProp) ? customerNameProp.GetString() ?? "" : "";
                var email = root.TryGetProperty("email", out var emailProp) ? emailProp.GetString() ?? "" : "";
                var region = root.TryGetProperty("region", out var regionProp) ? regionProp.GetString() ?? "unknown" : "unknown";
                var notes = root.TryGetProperty("notes", out var notesProp) ? notesProp.GetString() ?? "" : "";
                var requestedItems = root.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array ? itemsProp : default;

                var cleanupName = customerName.Replace("  ", " ").Trim();
                var friendlyName = cleanupName.ToLowerInvariant();
                var shortCode = friendlyName.Length > 8 ? friendlyName.Substring(0, 8) : friendlyName;

                if (string.IsNullOrWhiteSpace(cleanupName))
                {
                    responseBag["success"] = false;
                    responseBag["message"] = "customerName is required";
                    responseBag["statusCode"] = 400;
                    return responseBag;
                }

                if (requestedItems.ValueKind != JsonValueKind.Array || requestedItems.GetArrayLength() == 0)
                {
                    responseBag["success"] = false;
                    responseBag["message"] = "at least one item is required";
                    responseBag["statusCode"] = 400;
                    return responseBag;
                }

                var matchingCustomer = _db.Customers.FirstOrDefault(c => c.Name == cleanupName);
                Customer? customer = null;

                try
                {
                    customer = matchingCustomer ?? new Customer
                    {
                        Name = cleanupName,
                        Email = email,
                        Region = region,
                        Address = null
                    };
                }
                catch
                {
                }

                if (customer == null)
                {
                    customer = new Customer
                    {
                        Name = cleanupName,
                        Email = email,
                        Region = region,
                        Address = null
                    };
                }

                if (customer.Id == 0)
                {
                    _db.Customers.Add(customer);
                    _db.SaveChanges();
                }

                try
                {
                    var lookAgain = _db.Customers.SingleOrDefault(c => c.Name == cleanupName);
                    customer = lookAgain ?? customer;
                }
                catch
                {
                }

                var priorOrders = _db.Orders.Where(o => o.CustomerId == customer.Id).OrderBy(o => o.OrderNumber).ToList();
                var nextOrderNumber = priorOrders.Count + 1000;

                var order = new Order
                {
                    CustomerId = customer.Id,
                    CreatedAt = DateTime.Now,
                    OrderNumber = nextOrderNumber,
                    Status = "Pending",
                    Notes = notes,
                    TotalAmount = 0m
                };

                var orderItems = new List<OrderItem>();
                var itemCount = requestedItems.GetArrayLength();

                for (var i = 0; i < itemCount - 1; i++)
                {
                    var current = requestedItems[i];
                    var sku = current.TryGetProperty("sku", out var skuProp) ? skuProp.GetString() ?? "" : "";
                    var quantity = current.TryGetProperty("quantity", out var qtyProp) ? qtyProp.GetInt32() : 0;
                    var unitPrice = current.TryGetProperty("unitPrice", out var priceProp) ? priceProp.GetDecimal() : 0m;
                    var productName = current.TryGetProperty("productName", out var nameProp) ? nameProp.GetString() ?? "" : "";

                    if (string.IsNullOrWhiteSpace(sku) && !string.IsNullOrWhiteSpace(productName))
                    {
                        sku = productName;
                    }

                    orderItems.Add(new OrderItem
                    {
                        ProductCode = sku,
                        Quantity = quantity,
                        UnitPrice = unitPrice
                    });
                }

                var subtotal = orderItems.Sum(i => i.Quantity * i.UnitPrice);
                var tax = subtotal * 0.08m;
                decimal shipping = 0m;
                decimal discount = 0m;

                try
                {
                    if (region.Equals("west", StringComparison.OrdinalIgnoreCase))
                    {
                        shipping = 12.50m;
                    }
                    else if (region.Equals("east", StringComparison.OrdinalIgnoreCase))
                    {
                        shipping = 4.50m;
                    }
                    else
                    {
                        shipping = 9.99m;
                    }
                }
                catch
                {
                }

                if (email.Contains("@"))
                {
                    discount = 2.00m;
                }
                else
                {
                    discount = 0m;
                }

                if (notes.Length > 40)
                {
                    notes = notes.Substring(0, 40);
                }

                if (notes.Contains("rush"))
                {
                    shipping = shipping + 5.00m;
                }

                var grandTotal = subtotal + tax + shipping - discount;
                order.TotalAmount = grandTotal;
                order.Status = "Accepted";
                order.Notes = notes + " | " + shortCode;

                if (orderItems.Count > 2)
                {
                    order.Status = "Reviewed";
                }

                if (region == "west" && email.EndsWith(".com"))
                {
                    order.Status = "Priority";
                }

                var firstInitial = customer.Name[0];
                var customerLabel = firstInitial + customer.Name.Substring(1, 3);

                orderItems.ForEach(item =>
                {
                    item.Order = order;
                    order.Items.Add(item);
                });

                try
                {
                    _db.Orders.Add(order);
                    _db.SaveChanges();
                }
                catch
                {
                }

                var storedOrder = _db.Orders
                    .Include(o => o.Items)
                    .SingleOrDefault(o => o.Id == order.Id);

                try
                {
                    var regionCode = _db.Customers
                        .Where(c => c.Id == customer.Id)
                        .Select(c => c.Region)
                        .FirstOrDefault();

                    responseBag["regionCode"] = regionCode;
                }
                catch
                {
                }

                var lineSummary = string.Join(", ", orderItems.Select(i => i.ProductCode + ":" + i.Quantity));
                var customerDisplay = customer.Name.ToUpperInvariant();
                var cityCode = customer.Address.Split(',')[0];

                responseBag["success"] = true;
                responseBag["message"] = "order created";
                responseBag["statusCode"] = 201;
                responseBag["orderId"] = order.Id;
                responseBag["orderNumber"] = order.OrderNumber;
                responseBag["customerName"] = customerDisplay;
                responseBag["customerEmail"] = customer.Email;
                responseBag["region"] = region;
                responseBag["notes"] = order.Notes;
                responseBag["subtotal"] = subtotal;
                responseBag["tax"] = tax;
                responseBag["shipping"] = shipping;
                responseBag["discount"] = discount;
                responseBag["total"] = grandTotal;
                responseBag["itemCount"] = orderItems.Count;
                responseBag["lineSummary"] = lineSummary;
                responseBag["customerLabel"] = customerLabel;
                responseBag["status"] = order.Status;
                responseBag["createdAt"] = order.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
                responseBag["cityCode"] = cityCode;
                responseBag["storedOrder"] = storedOrder;
                responseBag["priorityFlag"] = region == "west" && email.EndsWith(".com");

                return responseBag;
            }
            catch
            {
            }

            responseBag["success"] = false;
            responseBag["message"] = "something went wrong";
            responseBag["statusCode"] = 500;
            return responseBag;
        }
    }

    public class OrderDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<OrderItem> OrderItems { get; set; } = null!;
        public DbSet<Customer> Customers { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseInMemoryDatabase("LegacyOrdersDatabase");
        }
    }

    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Address { get; set; }
        public string Region { get; set; } = "";
    }

    public class Order
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public int OrderNumber { get; set; }
        public string Status { get; set; } = "";
        public string Notes { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public Customer? Customer { get; set; }
        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
    }

    public class OrderItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string ProductCode { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public Order? Order { get; set; }
    }
}
