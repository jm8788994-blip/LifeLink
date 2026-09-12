using System.Text.RegularExpressions;
using LifeLink.Data;
using LifeLink.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services
{
    public class AiChatbotService : IAiChatbotService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAiMatchingService _matchingService;

        public AiChatbotService(ApplicationDbContext context, IAiMatchingService matchingService)
        {
            _context = context;
            _matchingService = matchingService;
        }

        public async Task<ChatbotMessageResponse> ProcessQueryAsync(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                return new ChatbotMessageResponse
                {
                    Answer = "Hello! I am LifeLink AI Assistant. How can I assist you with blood donation or requests today?",
                    QuickReplies = new() { "How to register?", "Check blood stock", "Emergency blood request", "Eligibility rules" }
                };
            }

            string q = userMessage.Trim().ToLowerInvariant();

            // 1. GREETINGS
            if (Regex.IsMatch(q, @"\b(hi|hello|hey|salam|assalamu|morning|afternoon)\b"))
            {
                return new ChatbotMessageResponse
                {
                    Answer = "Hello! Welcome to <strong>LifeLink</strong> - AI Powered Blood Donation System. I can help you find donors, check live blood bag inventory, explain eligibility, or guide you through registration.",
                    QuickReplies = new() { "Check blood stock", "How to donate blood?", "Create emergency request", "Donor eligibility" }
                };
            }

            // 2. CHECK BLOOD STOCK (LIVE DATABASE QUERY)
            var bloodGroupMatch = Regex.Match(q, @"\b(a\+|a\-|b\+|b\-|ab\+|ab\-|o\+|o\-)\b", RegexOptions.IgnoreCase);
            if (q.Contains("stock") || q.Contains("available") || q.Contains("availability") || q.Contains("koto bag") || q.Contains("ase ki") || q.Contains("ache"))
            {
                if (bloodGroupMatch.Success)
                {
                    string targetBg = bloodGroupMatch.Value.ToUpperInvariant();
                    int totalUnits = await _context.BloodStock
                        .Where(s => s.BloodGroup == targetBg)
                        .SumAsync(s => (int?)s.Quantity) ?? 0;

                    var hospitalsWithStock = await _context.BloodStock
                        .Include(s => s.Hospital)
                        .Where(s => s.BloodGroup == targetBg && s.Quantity > 0)
                        .Take(3)
                        .Select(s => $"{s.Hospital!.Name}: <strong>{s.Quantity} bags</strong>")
                        .ToListAsync();

                    string hospitalDetails = hospitalsWithStock.Any()
                        ? "<br/>Top hospitals with stock: <ul>" + string.Join("", hospitalsWithStock.Select(h => $"<li>{h}</li>")) + "</ul>"
                        : "<br/>Currently reserves are running low in registered hospitals.";

                    return new ChatbotMessageResponse
                    {
                        Answer = $"There are currently <strong>{totalUnits} units</strong> of <strong>{targetBg}</strong> blood available across the network.{hospitalDetails}",
                        QuickReplies = new() { "View all blood availability", "Request this blood group", "Who can donate to " + targetBg }
                    };
                }
                else
                {
                    int totalAll = await _context.BloodStock.SumAsync(s => (int?)s.Quantity) ?? 0;
                    return new ChatbotMessageResponse
                    {
                        Answer = $"Currently, LifeLink network has <strong>{totalAll} total blood bags</strong> in inventory across all groups. Which specific blood group would you like to check (e.g. O+, A+, B+, AB-)?",
                        QuickReplies = new() { "Check O+ stock", "Check B+ stock", "Check A+ stock", "Check AB+ stock" }
                    };
                }
            }

            // 3. HOW TO REGISTER AS A DONOR (PDF 4.8 Query 1)
            if (q.Contains("register") || q.Contains("become a donor") || q.Contains("join") || q.Contains("registration"))
            {
                return new ChatbotMessageResponse
                {
                    Answer = "Registering as a life-saving donor is quick and simple: <ol><li>Click on <strong>Register</strong> in the top menu.</li><li>Select <strong>'Blood Donor'</strong>.</li><li>Fill in your name, contact, blood group, and area.</li><li>Verify your account with the instant 6-digit OTP code.</li></ol>Once registered, your status is activated and nearby emergency patients can connect with you!",
                    QuickReplies = new() { "Am I eligible to donate?", "Check blood stock", "How does AI matching work?" },
                    ActionUrl = "/Account/Register"
                };
            }

            // 4. HOW TO CREATE A BLOOD REQUEST (PDF 4.8 Query 2)
            if (q.Contains("request") || q.Contains("need blood") || q.Contains("emergency") || q.Contains("patient") || q.Contains("rokto lagbe"))
            {
                return new ChatbotMessageResponse
                {
                    Answer = "To submit an emergency blood request: <ol><li>Log in or register as a <strong>Patient/Receiver</strong>.</li><li>Go to your <strong>Receiver Dashboard</strong> and click <strong>'New Blood Request'</strong>.</li><li>Specify the required blood group, units, hospital, and urgency level (Normal/Urgent/Critical).</li><li>Our <strong>AI Smart Matching Engine</strong> will instantly evaluate nearby donors and broadcast emergency alerts!</li></ol>",
                    QuickReplies = new() { "Request blood now", "Find blood donors", "Emergency contacts" },
                    IsEmergency = true,
                    ActionUrl = "/Account/Register?role=Receiver"
                };
            }

            // 5. DONOR ELIGIBILITY RULES & 90-DAY INTERVAL (PDF 4.8 Query 3 & 4.13)
            if (q.Contains("eligib") || q.Contains("rule") || q.Contains("who can donate") || q.Contains("days") || q.Contains("koto din por"))
            {
                return new ChatbotMessageResponse
                {
                    Answer = "<strong>Medical Eligibility Guidelines:</strong> <ul><li><strong>Interval:</strong> At least <strong>90 days (3 months)</strong> must pass between whole blood donations.</li><li><strong>Age:</strong> Between 18 and 60 years old.</li><li><strong>Weight:</strong> Minimum 50 kg (110 lbs).</li><li><strong>Hemoglobin:</strong> Minimum 12.5 g/dL.</li><li><strong>General Health:</strong> Free of cold, fever, or active medication on donation day.</li></ul>Donors can view their personal real-time countdown badge on their <strong>Donor Dashboard</strong>.",
                    QuickReplies = new() { "Register as donor", "How to update availability?", "Check blood compatibility" }
                };
            }

            // 6. BLOOD COMPATIBILITY QUESTIONS
            if (q.Contains("compatible") || q.Contains("who can give") || q.Contains("can i give") || q.Contains("who can receive"))
            {
                if (bloodGroupMatch.Success)
                {
                    string bg = bloodGroupMatch.Value.ToUpperInvariant();
                    string explanation = bg switch
                    {
                        "O-" => "<strong>O- is the Universal Donor!</strong> You can safely donate blood to ANY blood group (O+, O-, A+, A-, B+, B-, AB+, AB-). However, you can ONLY receive from O-.",
                        "O+" => "<strong>O+</strong> can donate to O+, A+, B+, and AB+. You can receive red blood cells from O+ and O-.",
                        "A+" => "<strong>A+</strong> can donate to A+ and AB+. You can receive from A+, A-, O+, and O-.",
                        "A-" => "<strong>A-</strong> can donate to A+, A-, AB+, and AB-. You can receive from A- and O-.",
                        "B+" => "<strong>B+</strong> can donate to B+ and AB+. You can receive from B+, B-, O+, and O-.",
                        "B-" => "<strong>B-</strong> can donate to B+, B-, AB+, and AB-. You can receive from B- and O-.",
                        "AB+" => "<strong>AB+ is the Universal Recipient!</strong> You can receive blood from ANY blood group. You can donate to AB+.",
                        "AB-" => "<strong>AB-</strong> can donate to AB+ and AB-. You can receive from AB-, A-, B-, and O-.",
                        _ => "Every blood group has specific compatible donors and recipients."
                    };

                    return new ChatbotMessageResponse
                    {
                        Answer = explanation,
                        QuickReplies = new() { "Check blood stock", "Find " + bg + " donors", "Register as donor" }
                    };
                }
                else
                {
                    return new ChatbotMessageResponse
                    {
                        Answer = "<strong>Universal Blood Summary:</strong> <ul><li><strong>O- Negative:</strong> Universal red cell donor (can give to anyone).</li><li><strong>AB+ Positive:</strong> Universal recipient (can safely receive from anyone).</li></ul>Which specific blood group would you like details about?",
                        QuickReplies = new() { "About O-", "About O+", "About AB+", "About B+" }
                    };
                }
            }

            // 7. HOW TO UPDATE AVAILABILITY (PDF 4.8 Query 3)
            if (q.Contains("availab") || q.Contains("switch") || q.Contains("active") || q.Contains("inactive") || q.Contains("busy"))
            {
                return new ChatbotMessageResponse
                {
                    Answer = "You can update your availability in 1 click! Simply log in to your <strong>Donor Dashboard</strong> and toggle the <strong>'Switch to Inactive / Active'</strong> button at the top of the screen. Inactive donors will not be disturbed by emergency notifications.",
                    QuickReplies = new() { "Go to Login", "Check donor benefits", "Donation history" }
                };
            }

            // 8. HOW TO CHECK DONATION HISTORY (PDF 4.8 Query 5)
            if (q.Contains("history") || q.Contains("record") || q.Contains("past donation") || q.Contains("certificate"))
            {
                return new ChatbotMessageResponse
                {
                    Answer = "Your verified donation records are tracked securely under <strong>Donor Dashboard &gt; Your Donation History</strong>. Each completed donation records the hospital name, date, and updates your medical resting countdown timer.",
                    QuickReplies = new() { "Go to Donor Dashboard", "Sign In", "Eligibility rules" }
                };
            }

            // 9. HOSPITALS DIRECTORY (PDF 4.9)
            if (q.Contains("hospital") || q.Contains("dhaka medical") || q.Contains("chittagong medical") || q.Contains("clinic"))
            {
                var hospitals = await _context.Hospitals.Take(4).ToListAsync();
                string list = string.Join("", hospitals.Select(h => $"<li><strong>{h.Name}</strong> ({h.Location}) - Tel: {h.Contact}</li>"));

                return new ChatbotMessageResponse
                {
                    Answer = $"LifeLink is integrated with top verified healthcare facilities: <ul>{list}</ul>",
                    QuickReplies = new() { "Check blood availability", "Emergency request", "How to register?" }
                };
            }

            // 10. AI SYSTEM EXPLANATION (PDF 4.4 - 4.7)
            if (q.Contains("ai") || q.Contains("smart match") || q.Contains("ml") || q.Contains("machine learning") || q.Contains("algorithm"))
            {
                return new ChatbotMessageResponse
                {
                    Answer = "LifeLink uses <strong>ML.NET (Machine Learning)</strong> to evaluate: <ol><li><strong>Blood Compatibility (35%)</strong></li><li><strong>Geographic Distance / Proximity (25%)</strong></li><li><strong>Medical 90-Day Eligibility (15%)</strong></li><li><strong>Donor Active Availability (15%)</strong></li><li><strong>Historical Donation Reliability (10%)</strong></li></ol>It predicts a personalized <strong>Match Score (%)</strong> and <strong>Response Probability</strong> so patients get blood quickly!",
                    QuickReplies = new() { "Check blood stock", "Find donors", "Become a donor" }
                };
            }

            // 11. DEFAULT INTELLIGENT FALLBACK
            return new ChatbotMessageResponse
            {
                Answer = "I'm here to help with all aspects of <strong>LifeLink Blood Donation System</strong>. You can ask me to check blood availability, guide you through registration, explain medical eligibility, or find hospital information.",
                QuickReplies = new() { "Check blood stock", "How to register?", "Emergency blood request", "Eligibility rules" }
            };
        }
    }
}
