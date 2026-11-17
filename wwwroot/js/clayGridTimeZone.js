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
    },

    /**
     * Преобразует местные календарную дату и время браузера в Unix UTC milliseconds.
     * @param {string} dateText дата в формате yyyy-MM-dd
     * @param {string} timeText время в формате HH:mm:ss
     * @returns {number} миллисекунды Unix UTC
     */
    localDateTimeToUtcMilliseconds: function (dateText, timeText) {
        const [year, month, day] = dateText.split('-').map(Number);
        const [hours, minutes, seconds = 0] = timeText.split(':').map(Number);
        return new Date(year, month - 1, day, hours, minutes, seconds, 0).getTime();
    }
};
