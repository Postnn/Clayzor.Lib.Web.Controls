// ── Часовой пояс клиента для ClayGrid (Тип 10/13) ─────────────────────────────
window.clayGridTimeZone = {
    /**
     * Смещение локального пояса от UTC в минутах.
     * getTimezoneOffset() возвращает ОБРАТНЫЙ знак (UTC+3 → -180), поэтому инвертируем.
     * @returns {number} минуты, напр. 180 для UTC+3
     */
    getOffsetMinutes: function () {
        return -new Date().getTimezoneOffset();
    }
};
