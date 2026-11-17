// ── Часовой пояс клиента для ClayGrid (Тип 10/13) ─────────────────────────────
window.clayGridTimeZone = {
    /**
     * Смещение локального пояса от UTC в минутах на указанную дату.
     * getTimezoneOffset() возвращает ОБРАТНЫЙ знак (UTC+3 → -180), поэтому инвертируем.
     * @param {string} [utcDate] UTC-дата записи; без неё используется текущий момент
     * @returns {number} минуты, напр. 180 для UTC+3
     */
    getOffsetMinutes: function (utcDate) {
        const date = utcDate === undefined ? new Date() : new Date(utcDate);
        return -date.getTimezoneOffset();
    }
};
