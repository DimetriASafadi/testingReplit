using System;

namespace NewGaza.Core
{
    public sealed class EconomyService
    {
        private const long DaySeconds = 86400;
        private const int MaximumFleet = 1000;
        public GameState State { get; private set; }

        public EconomyService(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state), "بيانات الحفظ مفقودة");
            State = state;
            ValidateState();
        }

        // A persisted high-water timestamp makes repeated ticks and clock rollback harmless.
        // Phase transitions are anchored to their scheduled deadlines, not the time of login.
        public void Tick(long now)
        {
            if (now < 0) throw new ArgumentOutOfRangeException(nameof(now), "وقت الجهاز غير صالح");
            ValidateState();
            long effective = Math.Max(now, State.lastSeenUtc);
            while (State.jobStage != JobStage.Idle && State.jobFinishUtc <= effective)
            {
                long boundary = State.jobFinishUtc;
                if (State.jobStage == JobStage.Clearing)
                {
                    State.jobStage = JobStage.Hauling;
                    State.jobFinishUtc = AddTime(boundary, HaulingSeconds());
                }
                else if (State.jobStage == JobStage.Hauling)
                {
                    State.jobStage = JobStage.Recycling;
                    State.jobFinishUtc = AddTime(boundary, RecyclingSeconds());
                }
                else
                {
                    int multiplier = State.factoryLevel;
                    if (!FitsStock(40 * multiplier, 15 * multiplier, 12 * multiplier, 8 * multiplier))
                    {
                        // Other actions can fill storage after a contract starts. Keep the
                        // finished batch intact and retry delivery on a later Tick, allowing
                        // spending/sales and normal project timers to continue meanwhile.
                        break;
                    }
                    AddStock(40 * multiplier, 15 * multiplier, 12 * multiplier, 8 * multiplier);
                    var district = State.districts[State.jobDistrict];
                    if (district.clearedLoads < GameCatalog.Districts[State.jobDistrict].rubbleLoads)
                        district.clearedLoads++;
                    State.jobStage = JobStage.Idle;
                    State.jobFinishUtc = 0;
                }
            }
            for (int d = 0; d < State.districts.Length; d++)
            {
                for (int p = 0; p < State.districts[d].projects.Length; p++)
                {
                    var project = State.districts[d].projects[p];
                    if (!project.completed && project.finishUtc > 0 && project.finishUtc <= effective)
                    {
                        project.completed = true;
                        var definition = GameCatalog.Districts[d].projects[p];
                        if (!IsBatch(definition)) project.lastIncomeUtc = project.finishUtc;
                    }
                }
            }
            State.lastSeenUtc = effective;
        }

        public ActionResult SelectDistrict(int index)
        {
            Tick(State.lastSeenUtc);
            if (!IsUnlocked(index)) return ActionResult.Fail("هذا الحي مقفل؛ أكمل الحي السابق واستلم مكافأته");
            State.selectedDistrict = index;
            return ActionResult.Ok("تم اختيار " + GameCatalog.Districts[index].name);
        }

        public ActionResult BuyEquipment(string kind, long now)
        {
            if (!Prepare(now)) return BadTime();
            long cost;
            switch (kind)
            {
                case "factory":
                    if (State.factoryLevel != 0) return ActionResult.Fail("المصنع موجود بالفعل؛ استخدم الترقية");
                    cost = GameCatalog.FactoryCost; break;
                case "excavator":
                    if (State.excavators >= MaximumFleet) return ActionResult.Fail("وصل أسطول الحفارات إلى الحد الأقصى");
                    cost = GameCatalog.ExcavatorCost; break;
                case "truck":
                    if (State.trucks >= MaximumFleet) return ActionResult.Fail("وصل أسطول الشاحنات إلى الحد الأقصى");
                    cost = GameCatalog.TruckCost; break;
                case "bulldozer":
                    if (State.bulldozers >= MaximumFleet) return ActionResult.Fail("وصل أسطول الجرافات إلى الحد الأقصى");
                    cost = GameCatalog.BulldozerCost; break;
                default: return ActionResult.Fail("نوع المعدة غير معروف");
            }
            if (!CanPay(cost, 0, 0)) return ActionResult.Fail("الرصيد لا يكفي لشراء المعدة");
            State.coins -= cost;
            switch (kind)
            {
                case "factory": State.factoryLevel = 1; break;
                case "excavator": State.excavators++; break;
                case "truck": State.trucks++; break;
                case "bulldozer": State.bulldozers++; break;
            }
            return ActionResult.Ok("تم شراء المعدة");
        }

        public ActionResult UpgradeFactory(long now)
        {
            if (!Prepare(now)) return BadTime();
            if (State.factoryLevel == 0) return ActionResult.Fail("اشتر مصنع التدوير أولاً");
            if (State.factoryLevel >= 5) return ActionResult.Fail("المصنع في المستوى الخامس بالفعل");
            if (State.jobStage != JobStage.Idle) return ActionResult.Fail("انتظر انتهاء عقد التدوير قبل ترقية المصنع");
            long cost = State.factoryLevel * 20000L;
            if (!CanPay(cost, 0, 0)) return ActionResult.Fail("الرصيد لا يكفي لترقية المصنع");
            State.coins -= cost;
            State.factoryLevel++;
            return ActionResult.Ok("تمت ترقية المصنع؛ إنتاج مواد أكثر وتدوير أسرع");
        }

        public ActionResult UpgradeEquipment(long now)
        {
            if (!Prepare(now)) return BadTime();
            if (State.excavators == 0 || State.trucks == 0 || State.bulldozers == 0)
                return ActionResult.Fail("اشتر حفارة وشاحنة وجرافة قبل ترقية المعدات");
            if (State.equipmentLevel >= 5) return ActionResult.Fail("المعدات في المستوى الخامس بالفعل");
            if (State.jobStage != JobStage.Idle) return ActionResult.Fail("انتظر انتهاء العقد قبل ترقية المعدات");
            long cost = State.equipmentLevel * 12000L;
            if (!CanPay(cost, 0, 0)) return ActionResult.Fail("الرصيد لا يكفي لترقية المعدات");
            State.coins -= cost;
            State.equipmentLevel++;
            return ActionResult.Ok("تمت ترقية المعدات؛ إزالة ونقل أسرع");
        }

        public ActionResult StartSalvage(int district, long now)
        {
            if (!Prepare(now)) return BadTime();
            if (!IsUnlocked(district)) return ActionResult.Fail("لا يمكن العمل في حي مقفل");
            if (State.jobStage != JobStage.Idle)
                return ActionResult.Fail(State.jobStage == JobStage.Recycling && State.jobFinishUtc <= State.lastSeenUtc
                    ? "اكتمل التدوير وينتظر مساحة في المخزن؛ بع بعض المواد لاستلام الإنتاج"
                    : "هناك عقد إزالة ونقل وتدوير قيد التنفيذ");
            if (State.factoryLevel == 0 || State.excavators == 0 || State.trucks == 0 || State.bulldozers == 0)
                return ActionResult.Fail("يلزم مصنع وحفارة وجرافة وشاحنة لإزالة الركام ونقله وتدويره");
            if (!FitsStock(40 * State.factoryLevel, 15 * State.factoryLevel, 12 * State.factoryLevel, 8 * State.factoryLevel))
                return ActionResult.Fail("المخزن ممتلئ؛ بع بعض المواد أولاً");
            long duration = ClearingSeconds();
            if (!CanSchedule(duration + HaulingSeconds() + RecyclingSeconds())) return BadTime();
            State.jobDistrict = district;
            State.jobStage = JobStage.Clearing;
            State.jobFinishUtc = State.lastSeenUtc + duration;
            bool imported = State.districts[district].clearedLoads >= GameCatalog.Districts[district].rubbleLoads;
            return ActionResult.Ok(imported
                ? "بدأ عقد تدوير ركام مستورد؛ يوفر مواد ولا يزيد إنجاز الحي"
                : "بدأت إزالة الركام؛ يليه النقل ثم التدوير تلقائياً");
        }

        public ActionResult SellResources(string kind, long now)
        {
            if (!Prepare(now)) return BadTime();
            bool all = kind == "all";
            if (!all && kind != "concrete" && kind != "iron" && kind != "wood" && kind != "other")
                return ActionResult.Fail("نوع المادة غير معروف");
            var stock = State.stock;
            long amount = (all || kind == "concrete" ? stock.concrete * 20L : 0)
                        + (all || kind == "iron" ? stock.iron * 60L : 0)
                        + (all || kind == "wood" ? stock.wood * 35L : 0)
                        + (all || kind == "other" ? stock.other * 10L : 0);
            if (amount == 0) return ActionResult.Fail("لا توجد مواد من هذا النوع للبيع");
            if (!FitsCoins(amount)) return ActionResult.Fail("الرصيد بلغ الحد الأقصى؛ لم يتم بيع المواد");
            State.coins += amount;
            if (all || kind == "concrete") stock.concrete = 0;
            if (all || kind == "iron") stock.iron = 0;
            if (all || kind == "wood") stock.wood = 0;
            if (all || kind == "other") stock.other = 0;
            return ActionResult.Ok("تم بيع المواد مقابل " + amount + " عملة");
        }

        public ActionResult StartProject(int district, string projectId, long now)
        {
            if (!Prepare(now)) return BadTime();
            if (!IsUnlocked(district)) return ActionResult.Fail("لا يمكن البناء في حي مقفل");
            int index = ProjectIndex(district, projectId);
            if (index < 0) return ActionResult.Fail("المشروع غير موجود");
            var definition = GameCatalog.Districts[district].projects[index];
            var project = State.districts[district].projects[index];
            if (!ValidDefinition(definition)) return ActionResult.Fail("بيانات تكلفة المشروع أو مدته غير صالحة");
            bool batch = IsBatch(definition);
            if (project.completed && !batch) return ActionResult.Fail("المشروع مكتمل بالفعل");
            if (project.finishUtc > 0 && (!project.completed || batch))
                return ActionResult.Fail(batch && project.finishUtc <= State.lastSeenUtc
                    ? "اجمع الحصاد أو الإنتاج قبل بدء دورة جديدة" : "المشروع قيد التنفيذ؛ انتظر الموعد");
            if (!string.IsNullOrEmpty(definition.prerequisite))
            {
                int prerequisiteIndex = ProjectIndex(district, definition.prerequisite);
                if (prerequisiteIndex < 0) return ActionResult.Fail("بيانات متطلبات المشروع غير صالحة");
                if (!State.districts[district].projects[prerequisiteIndex].completed)
                    return ActionResult.Fail("أكمل المشروع المطلوب أولاً: " + GameCatalog.Districts[district].projects[prerequisiteIndex].name);
            }
            if (!CanPay(definition.cost, definition.concreteCost, definition.ironCost))
                return ActionResult.Fail("الرصيد أو الخرسانة أو الحديد لا يكفي؛ خزّن مواد التدوير أو بع الفائض");
            if (!CanSchedule(definition.durationSeconds)) return BadTime();
            State.coins -= definition.cost;
            State.stock.concrete -= definition.concreteCost;
            State.stock.iron -= definition.ironCost;
            project.startedUtc = State.lastSeenUtc;
            project.finishUtc = State.lastSeenUtc + definition.durationSeconds;
            if (batch) project.lastIncomeUtc = 0;
            return ActionResult.Ok(batch ? "بدأت دورة الزراعة أو الإنتاج؛ اجمعها بعد انتهاء الوقت" : "بدأ البناء؛ يستمر الوقت أثناء غيابك");
        }

        public ActionResult CollectIncome(int district, string projectId, long now)
        {
            if (!Prepare(now)) return BadTime();
            if (!IsUnlocked(district)) return ActionResult.Fail("الحي مقفل");
            int index = ProjectIndex(district, projectId);
            if (index < 0) return ActionResult.Fail("المشروع غير موجود");
            var definition = GameCatalog.Districts[district].projects[index];
            if (!ValidDefinition(definition)) return ActionResult.Fail("بيانات دخل المشروع غير صالحة");
            var project = State.districts[district].projects[index];
            long amount = PendingIncome(district, projectId, State.lastSeenUtc);
            if (amount <= 0) return ActionResult.Fail("لا يوجد دخل جاهز؛ انتظر اكتمال البناء أو الحصاد أو دورة الدخل");
            if (!IsBatch(definition)
                && (State.lastSeenUtc - project.lastIncomeUtc) / definition.incomeSeconds > long.MaxValue / definition.income)
                return ActionResult.Fail("الدخل المتراكم يتجاوز سعة الرصيد؛ لم يتم جمعه");
            bool industry = IsIndustry(definition);
            if (!FitsCoins(amount) || (industry && !FitsStock(80, 30, 20, 10)))
                return ActionResult.Fail("الرصيد أو المخزن بلغ الحد الأقصى؛ لم يتم جمع الإنتاج");
            State.coins += amount;
            if (IsBatch(definition))
            {
                if (industry) AddStock(80, 30, 20, 10);
                project.startedUtc = 0;
                project.finishUtc = 0;
                project.lastIncomeUtc = State.lastSeenUtc;
            }
            else
            {
                // Preserve the fractional remainder instead of discarding it at collection.
                long periods = (State.lastSeenUtc - project.lastIncomeUtc) / definition.incomeSeconds;
                project.lastIncomeUtc += periods * definition.incomeSeconds;
            }
            return ActionResult.Ok(industry ? "تم جمع الدخل والمواد المصنعة؛ يمكنك بدء دفعة جديدة" : "تم جمع " + amount + " عملة");
        }

        public ActionResult ClaimDistrictReward(int district, long now)
        {
            if (!Prepare(now)) return BadTime();
            if (!IsUnlocked(district)) return ActionResult.Fail("الحي مقفل");
            var state = State.districts[district];
            if (state.rewardClaimed) return ActionResult.Fail("تم استلام مكافأة هذا الحي سابقاً");
            if (!IsComplete(district)) return ActionResult.Fail("أكمل إزالة الركام وجميع مشاريع الحي بنسبة ١٠٠٪ أولاً");
            long reward = GameCatalog.Districts[district].completionReward;
            if (reward <= 0 || !FitsCoins(reward)) return ActionResult.Fail("قيمة المكافأة غير صالحة أو الرصيد ممتلئ");
            State.coins += reward;
            state.rewardClaimed = true;
            if (district < 9) State.districts[district + 1].unlocked = true;
            else if (district == 9)
            {
                bool all = true;
                for (int i = 0; i < 10; i++) all &= State.districts[i].rewardClaimed;
                if (all) State.districts[10].unlocked = true;
            }
            else State.cityCompletedUtc = State.lastSeenUtc;
            return ActionResult.Ok(district == 10 ? "اكتملت إعادة بناء المدينة وواجهة الرشيد!" : "تم استلام المكافأة وفتح الحي التالي");
        }

        public ActionResult ClaimDailyGift(long now)
        {
            bool rollback = now < State.lastSeenUtc;
            if (!Prepare(now)) return BadTime();
            if (rollback || !CanClaimDailyGift(now)) return ActionResult.Fail("الهدية متاحة كل ٢٤ ساعة؛ تحقق من وقت الجهاز وانتظر الموعد");
            int completed = 0;
            for (int i = 0; i < State.districts.Length; i++)
                if (IsUnlocked(i) && IsComplete(i)) completed++;
            long coins = 3000 + completed * 1000L;
            int concrete = 20 + completed * 5;
            int iron = 5 + completed * 2;
            if (!FitsCoins(coins) || !FitsStock(concrete, iron, 5, 3))
                return ActionResult.Fail("الرصيد أو المخزن ممتلئ؛ لم يتم استلام الهدية");
            State.coins += coins;
            AddStock(concrete, iron, 5, 3);
            State.lastGiftUtc = State.lastSeenUtc;
            return ActionResult.Ok("تم استلام هدية يومية مضمونة من العملات والمواد");
        }

        public float Progress(int district)
        {
            if (!ValidDistrict(district)) return 0f;
            var state = State.districts[district];
            var definition = GameCatalog.Districts[district];
            if (!IsUnlocked(district)) return 0f;
            int complete = 0;
            for (int i = 0; i < state.projects.Length; i++)
                if (state.projects[i].completed) complete++;
            if (complete == state.projects.Length && state.clearedLoads >= definition.rubbleLoads) return 1f;
            float rubble = definition.rubbleLoads == 0 ? 1f : Math.Min(1f, (float)state.clearedLoads / definition.rubbleLoads);
            return Math.Max(0f, Math.Min(1f, rubble * 0.3f + (float)complete / state.projects.Length * 0.7f));
        }

        public long PendingIncome(int district, string projectId, long now)
        {
            if (now < 0 || !IsUnlocked(district)) return 0;
            int index = ProjectIndex(district, projectId);
            if (index < 0) return 0;
            var definition = GameCatalog.Districts[district].projects[index];
            var project = State.districts[district].projects[index];
            if (!ValidDefinition(definition) || definition.income <= 0) return 0;
            // Read-only queries also work before Tick, but do not generate future/rollback income.
            if (project.finishUtc > now || (!project.completed && project.finishUtc == 0)) return 0;
            if (IsBatch(definition))
                return project.finishUtc > 0 && project.lastIncomeUtc < project.finishUtc ? definition.income : 0;
            if (definition.incomeSeconds <= 0) return 0;
            long baseline = project.completed ? project.lastIncomeUtc : project.finishUtc;
            if (now <= baseline) return 0;
            long periods = (now - baseline) / definition.incomeSeconds;
            return periods > long.MaxValue / definition.income ? long.MaxValue : periods * definition.income;
        }

        public bool CanClaimDailyGift(long now)
        {
            if (now <= 0 || now < State.lastSeenUtc || now < State.lastGiftUtc) return false;
            return State.lastGiftUtc == 0 || now - State.lastGiftUtc >= DaySeconds;
        }

        public ProjectState FindProject(int district, string projectId)
        {
            int index = ProjectIndex(district, projectId);
            return index < 0 ? null : State.districts[district].projects[index];
        }

        private bool Prepare(long now)
        {
            if (now < 0) return false;
            Tick(now);
            return true;
        }

        private static ActionResult BadTime() { return ActionResult.Fail("وقت الجهاز غير صالح؛ لم يبدأ أي إجراء"); }
        private bool ValidDistrict(int district) { return district >= 0 && district < State.districts.Length; }
        private bool IsUnlocked(int district)
        {
            if (!ValidDistrict(district) || !State.districts[district].unlocked) return false;
            for (int i = 0; i < district; i++)
                if (!State.districts[i].rewardClaimed) return false;
            return true;
        }

        private int ProjectIndex(int district, string id)
        {
            if (!ValidDistrict(district) || string.IsNullOrEmpty(id)) return -1;
            var definitions = GameCatalog.Districts[district].projects;
            for (int i = 0; i < definitions.Length; i++)
                if (string.Equals(definitions[i].id, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        private bool IsComplete(int district)
        {
            if (State.districts[district].clearedLoads < GameCatalog.Districts[district].rubbleLoads) return false;
            foreach (var project in State.districts[district].projects)
                if (!project.completed) return false;
            return true;
        }

        private static bool IsBatch(ProjectDefinition definition)
        {
            return definition.kind == ProjectKind.Investment && (definition.id == "farm" || definition.id == "industry");
        }
        private static bool IsIndustry(ProjectDefinition definition)
        {
            return definition.kind == ProjectKind.Investment && definition.id == "industry";
        }
        private static bool ValidDefinition(ProjectDefinition definition)
        {
            return definition != null && !string.IsNullOrEmpty(definition.id) && definition.cost >= 0
                && definition.durationSeconds > 0 && definition.concreteCost >= 0 && definition.ironCost >= 0
                && definition.income >= 0 && definition.incomeSeconds >= 0
                && definition.kind >= ProjectKind.Housing && definition.kind <= ProjectKind.Landmark
                && (definition.income == 0 || IsBatch(definition) || definition.incomeSeconds > 0);
        }
        private bool CanPay(long cost, int concrete, int iron)
        {
            return cost >= 0 && concrete >= 0 && iron >= 0 && State.coins >= cost
                && State.stock.concrete >= concrete && State.stock.iron >= iron;
        }
        private bool FitsCoins(long amount) { return amount >= 0 && State.coins <= long.MaxValue - amount; }
        private bool FitsStock(int concrete, int iron, int wood, int other)
        {
            return State.stock.concrete <= int.MaxValue - concrete && State.stock.iron <= int.MaxValue - iron
                && State.stock.wood <= int.MaxValue - wood && State.stock.other <= int.MaxValue - other;
        }
        private void AddStock(int concrete, int iron, int wood, int other)
        {
            State.stock.concrete += concrete; State.stock.iron += iron;
            State.stock.wood += wood; State.stock.other += other;
        }
        private long ClearingSeconds()
        {
            return Math.Max(1L, 180L / ((long)State.excavators * State.bulldozers * State.equipmentLevel));
        }
        private long HaulingSeconds() { return Math.Max(1L, 120L / ((long)State.trucks * State.equipmentLevel)); }
        private long RecyclingSeconds() { return Math.Max(1L, 240L / State.factoryLevel); }
        private bool CanSchedule(long seconds) { return seconds > 0 && State.lastSeenUtc <= long.MaxValue - seconds; }
        private static long AddTime(long timestamp, long seconds)
        {
            return timestamp > long.MaxValue - seconds ? long.MaxValue : timestamp + seconds;
        }

        private void ValidateState()
        {
            const string error = "بيانات الحفظ غير صالحة؛ لم تتم إعادة ضبط تقدمك";
            if (State.version != 1 || State.coins < 0 || State.stock == null
                || State.stock.concrete < 0 || State.stock.iron < 0 || State.stock.wood < 0 || State.stock.other < 0
                || State.factoryLevel < 0 || State.factoryLevel > 5 || State.equipmentLevel < 1 || State.equipmentLevel > 5
                || State.excavators < 0 || State.excavators > MaximumFleet || State.trucks < 0 || State.trucks > MaximumFleet
                || State.bulldozers < 0 || State.bulldozers > MaximumFleet || State.lastSeenUtc < 0 || State.lastGiftUtc < 0
                || State.lastGiftUtc > State.lastSeenUtc || State.cityCompletedUtc < 0 || State.cityCompletedUtc > State.lastSeenUtc
                || State.districts == null || State.districts.Length != GameCatalog.Districts.Length
                || State.selectedDistrict < 0 || State.selectedDistrict >= GameCatalog.Districts.Length)
                throw new InvalidOperationException(error);
            for (int d = 0; d < State.districts.Length; d++)
            {
                var district = State.districts[d];
                var definition = GameCatalog.Districts[d];
                if (district == null || district.clearedLoads < 0 || district.clearedLoads > definition.rubbleLoads
                    || district.projects == null || district.projects.Length != definition.projects.Length
                    || (d == 0 && !district.unlocked) || (district.rewardClaimed && !district.unlocked))
                    throw new InvalidOperationException(error);
                if (district.unlocked)
                    for (int previous = 0; previous < d; previous++)
                        if (!State.districts[previous].rewardClaimed) throw new InvalidOperationException(error);
                for (int p = 0; p < district.projects.Length; p++)
                {
                    var project = district.projects[p];
                    if (project == null || project.id != definition.projects[p].id
                        || project.startedUtc < 0 || project.finishUtc < 0 || project.lastIncomeUtc < 0
                        || project.startedUtc > State.lastSeenUtc || project.lastIncomeUtc > State.lastSeenUtc
                        || (project.finishUtc > 0 && project.finishUtc <= project.startedUtc)
                        || (!district.unlocked && (project.completed || project.finishUtc != 0 || project.startedUtc != 0))
                        || (!project.completed && project.lastIncomeUtc != 0)
                        || (!project.completed && project.finishUtc == 0 && project.startedUtc != 0))
                        throw new InvalidOperationException(error);
                    if (project.completed)
                    {
                        if (IsBatch(definition.projects[p]))
                        {
                            if ((project.finishUtc == 0 && (project.lastIncomeUtc == 0 || project.startedUtc != 0))
                                || (project.finishUtc > 0 && project.lastIncomeUtc != 0))
                                throw new InvalidOperationException(error);
                        }
                        else if (project.finishUtc == 0 || project.finishUtc > State.lastSeenUtc
                            || project.lastIncomeUtc < project.finishUtc)
                            throw new InvalidOperationException(error);
                    }
                }
                if (district.rewardClaimed && !IsComplete(d)) throw new InvalidOperationException(error);
            }
            if (State.jobStage < JobStage.Idle || State.jobStage > JobStage.Recycling || State.jobFinishUtc < 0
                || (State.jobStage != JobStage.Idle && (!IsUnlocked(State.jobDistrict) || State.jobFinishUtc == 0
                    || State.factoryLevel == 0 || State.excavators == 0 || State.trucks == 0 || State.bulldozers == 0)))
                throw new InvalidOperationException(error);
            if (!IsUnlocked(State.selectedDistrict) || (State.cityCompletedUtc > 0 && !State.districts[10].rewardClaimed))
                throw new InvalidOperationException(error);
        }
    }
}